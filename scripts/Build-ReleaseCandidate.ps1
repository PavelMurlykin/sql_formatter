param(
    [string]$OutputDirectory = 'artifacts/release-candidate',
    [string]$RestoreSource = '',
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
}
if (Test-Path -LiteralPath $output) {
    if (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1) {
        throw "Output directory is not empty: $output. Choose a new directory; this script never removes existing artifacts."
    }
} else {
    New-Item -ItemType Directory -Path $output -Force | Out-Null
}

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Read-VsixIdentity {
    param([string]$Path, [string]$ExpectedId, [string]$ExpectedTarget)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('extension.vsixmanifest')
        if ($null -eq $entry) { throw "VSIX manifest missing: $Path" }
        foreach ($required in @("$ExpectedId.dll", "$ExpectedId.pkgdef",
            'TSqlFormatter.Core.dll', 'TSqlFormatter.Configuration.dll',
            'Microsoft.SqlServer.TransactSql.ScriptDom.dll', 'Newtonsoft.Json.dll')) {
            if ($null -eq $archive.GetEntry($required)) { throw "VSIX asset missing: $required in $Path" }
        }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $identity = $manifest.PackageManifest.Metadata.Identity
        if ($identity.Id -ne $ExpectedId) { throw "Unexpected VSIX identity in ${Path}: $($identity.Id)" }
        $target = $manifest.PackageManifest.Installation.InstallationTarget
        if ($target.Id -ne $ExpectedTarget -or $target.ProductArchitecture -ne 'amd64') {
            throw "Unexpected VSIX installation target in $Path."
        }
        return [string]$identity.Version
    } finally {
        $archive.Dispose()
    }
}

Push-Location $repository
try {
    if (-not $SkipRestore) {
        $restoreArguments = @('restore', 'TSqlFormatter.sln')
        if (-not [string]::IsNullOrWhiteSpace($RestoreSource)) {
            if (-not (Test-Path -LiteralPath $RestoreSource)) { throw "Restore source not found: $RestoreSource" }
            $restoreArguments += @('--source', [System.IO.Path]::GetFullPath($RestoreSource))
        }
        Invoke-Dotnet -Arguments $restoreArguments
    }
    Invoke-Dotnet -Arguments @('build', 'TSqlFormatter.sln', '-c', 'Release', '--no-restore')
    Invoke-Dotnet -Arguments @('test', 'TSqlFormatter.sln', '-c', 'Release', '--no-build', '--no-restore')
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify-Vsix.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Visual Studio VSIX verification failed.' }

    [xml]$cliProject = Get-Content -LiteralPath 'src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj' -Raw
    $cliVersion = [string]$cliProject.Project.PropertyGroup.Version
    Invoke-Dotnet -Arguments @('pack', 'src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj', '-c', 'Release',
        '--no-build', '--no-restore', '-o', $output)
    $cliPackage = Join-Path $output "TSqlFormatter.Tool.$cliVersion.nupkg"
    if (-not (Test-Path -LiteralPath $cliPackage)) { throw "CLI package missing: $cliPackage" }

    $vsixSource = 'src/TSqlFormatter.VisualStudio/bin/Release/net472/TSqlFormatter.VisualStudio.vsix'
    $ssmsSource = 'src/TSqlFormatter.Ssms/bin/Release/net472/TSqlFormatter.Ssms.vsix'
    $vsixPackage = Join-Path $output 'TSqlFormatter.VisualStudio.vsix'
    $ssmsPackage = Join-Path $output 'TSqlFormatter.Ssms.vsix'
    Copy-Item -LiteralPath $vsixSource -Destination $vsixPackage
    Copy-Item -LiteralPath $ssmsSource -Destination $ssmsPackage
    $vsixVersion = Read-VsixIdentity $vsixPackage 'TSqlFormatter.VisualStudio' 'Microsoft.VisualStudio.Community'
    $ssmsVersion = Read-VsixIdentity $ssmsPackage 'TSqlFormatter.Ssms' 'Microsoft.VisualStudio.Ssms'

    $smokeTool = Join-Path $output '_smoke-tool'
    New-Item -ItemType Directory -Path $smokeTool | Out-Null
    Invoke-Dotnet -Arguments @('tool', 'install', 'TSqlFormatter.Tool', '--tool-path', $smokeTool,
        '--source', $output, '--version', $cliVersion)
    $tool = Join-Path $smokeTool 'tsqlformat.exe'
    $formatted = 'select 1;' | & $tool -
    if ($LASTEXITCODE -ne 0 -or ($formatted | Out-String).Trim() -ne 'SELECT 1;') {
        throw "Installed CLI smoke test failed: $formatted"
    }

    $packages = @(
        @{ Path = $cliPackage; Version = $cliVersion; Id = 'TSqlFormatter.Tool' },
        @{ Path = $vsixPackage; Version = $vsixVersion; Id = 'TSqlFormatter.VisualStudio' },
        @{ Path = $ssmsPackage; Version = $ssmsVersion; Id = 'TSqlFormatter.Ssms' }
    ) | ForEach-Object {
        [ordered]@{
            id = $_.Id
            version = $_.Version
            file = [System.IO.Path]::GetFileName($_.Path)
            sha256 = (Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        label = 'local-release-candidate'
        packages = $packages
        note = 'Local verification only; no package or extension was published.'
    }
    $manifestPath = Join-Path $output 'candidate-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Output "Release candidate verified: $output"
    Write-Output "CLI $cliVersion, Visual Studio VSIX $vsixVersion, SSMS VSIX $ssmsVersion"
    Write-Output "Checksums: $manifestPath"
} finally {
    Pop-Location
}
