param(
    [string]$IdeDirectory = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft SQL Server Management Studio 20\Common7\IDE'),
    [string]$PackagePath = (Join-Path $PSScriptRoot 'TSqlFormatter.SSMS20.vsix')
)

$ErrorActionPreference = 'Stop'
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
    if (Test-Path -LiteralPath $destination) {
        if (-not (Test-Path -LiteralPath $marker) -or
            (Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).id -ne 'TSqlFormatter.Ssms20') {
            throw "An unmanaged folder exists: $destination. Review it before updating."
        }
        $backup = Join-Path $ide ('TSqlFormatter.Ssms20-backup-' + [Guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $destination -Destination $backup -Recurse
        Write-Output "Previous extension backed up to $backup"
    }
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
