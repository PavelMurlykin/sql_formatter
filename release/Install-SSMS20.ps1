param(
    [string]$IdeDirectory = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft SQL Server Management Studio 20\Common7\IDE'),
    [string]$PackagePath = (Join-Path $PSScriptRoot 'TSqlFormatter.SSMS20.vsix'),
    [string]$BackupDirectory = (Join-Path $env:LOCALAPPDATA 'TSqlFormatter\SSMS20\Backups')
)

$ErrorActionPreference = 'Stop'

function Backup-SSMS20Extension {
    param([string]$IdeDirectory, [string]$BackupDirectory)

    $idePath = [IO.Path]::GetFullPath($IdeDirectory).TrimEnd('\')
    $backupRoot = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
    # SSMS recursively discovers pkgdef files beneath its IDE directory, even
    # outside Extensions. A backup there can override the current CodeBase.
    if ($backupRoot.Equals($idePath, [StringComparison]::OrdinalIgnoreCase) -or
        $backupRoot.StartsWith($idePath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'BackupDirectory must be outside the SSMS IDE directory and its package search paths.'
    }
    $installed = Join-Path $idePath 'Extensions\TSqlFormatter.Ssms20'
    $legacy = @(Get-ChildItem -LiteralPath $idePath -Directory -Filter 'TSqlFormatter.Ssms20-backup-*')
    $managed = @($legacy | ForEach-Object { $_.FullName })
    if (Test-Path -LiteralPath $installed) { $managed += $installed }
    foreach ($folder in $managed) {
        $managedMarker = Join-Path $folder 'tsqlformatter-install.json'
        if (-not (Test-Path -LiteralPath $managedMarker) -or
            (Get-Content -LiteralPath $managedMarker -Raw | ConvertFrom-Json).id -ne 'TSqlFormatter.Ssms20') {
            throw "An unmanaged folder exists: $folder. Review it before updating."
        }
    }
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    foreach ($folder in $legacy) {
        $source = [IO.Path]::GetFullPath($folder.FullName)
        $target = [IO.Path]::GetFullPath((Join-Path $backupRoot ($folder.Name + '-' + [Guid]::NewGuid().ToString('N'))))
        if (-not $source.StartsWith($idePath + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not $target.StartsWith($backupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Backup migration path is outside its expected directory.'
        }
        Move-Item -LiteralPath $source -Destination $target
        Write-Output "Legacy extension backup moved to $target"
    }
    if (Test-Path -LiteralPath $installed) {
        $backup = Join-Path $backupRoot ('TSqlFormatter.Ssms20-' + [Guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $installed -Destination $backup -Recurse
        Write-Output "Previous extension backed up to $backup"
    }
}

$ide = [IO.Path]::GetFullPath($IdeDirectory).TrimEnd('\')
$exe = Join-Path $ide 'Ssms.exe'
if (-not (Test-Path -LiteralPath $exe) -or (Get-Item -LiteralPath $exe).VersionInfo.ProductMajorPart -ne 20) {
    throw "This installer requires SSMS 20: $exe"
}
if (Get-Process -Name Ssms -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) {
    throw 'Close SSMS 20 before installing or updating the extension.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as Administrator. SSMS 20 loads extensions from its protected installation folder.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
$destination = Join-Path $ide 'Extensions\TSqlFormatter.Ssms20'
$marker = Join-Path $destination 'tsqlformatter-install.json'
try {
    $entry = $package.GetEntry('extension.vsixmanifest')
    if ($null -eq $entry) { throw 'Package manifest is missing.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.PackageManifest.Metadata.Identity.Id -ne 'TSqlFormatter.Ssms20') {
        throw 'Select the SSMS 20 package, TSqlFormatter.SSMS20.vsix.'
    }
    foreach ($required in @('TSqlFormatter.Ssms20.dll', 'TSqlFormatter.Ssms20.pkgdef',
        'TSqlFormatter.Core.dll', 'TSqlFormatter.Configuration.dll',
        'Microsoft.SqlServer.TransactSql.ScriptDom.dll', 'Newtonsoft.Json.dll')) {
        if ($null -eq $package.GetEntry($required)) { throw "Package asset missing: $required" }
    }
    $prefix = [IO.Path]::GetFullPath($destination).TrimEnd('\') + '\'
    foreach ($asset in $package.Entries) {
        $path = [IO.Path]::GetFullPath((Join-Path $destination $asset.FullName))
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe package entry: $($asset.FullName)"
        }
    }
    Backup-SSMS20Extension -IdeDirectory $ide -BackupDirectory $BackupDirectory
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($asset in $package.Entries) {
        if ($asset.FullName.EndsWith('/')) { continue }
        $path = Join-Path $destination $asset.FullName
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($asset, $path, $true)
    }
    @{ id = 'TSqlFormatter.Ssms20'; version = [string]$manifest.PackageManifest.Metadata.Identity.Version } |
        ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
} finally { $package.Dispose() }
Write-Output "Installed to $destination. Start SSMS 20 and open the top-level SQL Formatter menu."
