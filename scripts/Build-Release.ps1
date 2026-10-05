param([string]$RestoreSource = '', [switch]$SkipRestore)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = Join-Path $repository 'release'
function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed: $LASTEXITCODE" }
}
Push-Location $repository
try {
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    if (-not $SkipRestore) {
        foreach ($project in @('TSqlFormatter.sln', 'tests\TSqlFormatter.ReleaseSmoke\TSqlFormatter.ReleaseSmoke.csproj', 'tests\TSqlFormatter.SettingsUiSmoke\TSqlFormatter.SettingsUiSmoke.csproj')) {
            $arguments = @('restore', $project)
            if ($RestoreSource) { $arguments += @('--source', [IO.Path]::GetFullPath($RestoreSource)) }
            Invoke-Dotnet $arguments
        }
    }
    Invoke-Dotnet @('build', 'TSqlFormatter.sln', '-c', 'Release', '--no-restore')
    Invoke-Dotnet @('test', 'TSqlFormatter.sln', '-c', 'Release', '--no-build', '--no-restore')

    $packages = @(
        @{ Host = 'VS2022'; Assembly = 'TSqlFormatter.VisualStudio2022'; Id = 'TSqlFormatter.VisualStudio'; Target = 'Microsoft.VisualStudio.Community'; Range = '[17.14,18.0)'; Architecture = 'amd64' },
        @{ Host = 'VS2026'; Assembly = 'TSqlFormatter.VisualStudio'; Id = 'TSqlFormatter.VisualStudio'; Target = 'Microsoft.VisualStudio.Community'; Range = '[18.0,19.0)'; Architecture = 'amd64' },
        @{ Host = 'SSMS20'; Assembly = 'TSqlFormatter.Ssms20'; Id = 'TSqlFormatter.Ssms20'; Target = 'ssms'; Range = '[1.0,2.0)'; Architecture = '' },
        @{ Host = 'SSMS22'; Assembly = 'TSqlFormatter.Ssms'; Id = 'TSqlFormatter.Ssms'; Target = 'Microsoft.VisualStudio.Ssms'; Range = '[22.0,23.0)'; Architecture = 'amd64' }
    )
    $deliverables = @()
    foreach ($package in $packages) {
        $path = Join-Path $release ("TSqlFormatter." + $package.Host + '.vsix')
        Copy-Item -LiteralPath ("src\" + $package.Assembly + '\bin\Release\net472\' + $package.Assembly + '.vsix') -Destination $path
        & (Join-Path $PSScriptRoot 'Verify-Vsix.ps1') -VsixPath $path -ExpectedId $package.Id -ExpectedAssembly $package.Assembly -ExpectedTarget $package.Target -ExpectedRange $package.Range -ExpectedArchitecture $package.Architecture
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $reader = [IO.StreamReader]::new($archive.GetEntry('extension.vsixmanifest').Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $version = [string]$manifest.PackageManifest.Metadata.Identity.Version
        } finally { $archive.Dispose() }
        $deliverables += [ordered]@{ file = [IO.Path]::GetFileName($path); host = $package.Host; version = $version }
    }
    foreach ($name in @('ADIR_SQL_Main', 'AV_Profile', 'Right-aligned-EPM-AWB2')) {
        Copy-Item -LiteralPath "examples\profiles\$name.json" -Destination $release
        $deliverables += [ordered]@{ file = "$name.json"; kind = 'native-formatting-profile' }
    }
    Copy-Item -LiteralPath 'scripts\Install-SSMS20.ps1' -Destination $release
    $deliverables += [ordered]@{ file = 'Install-SSMS20.ps1'; kind = 'SSMS20-installer' }
    & (Join-Path $PSScriptRoot 'Verify-ReleaseProfiles.ps1') -ReleaseDirectory $release
    & (Join-Path $PSScriptRoot 'Verify-SettingsUi.ps1') -SkipRestore
    if ($LASTEXITCODE -ne 0) { throw 'Settings UI verification failed.' }

    $cliOutput = Join-Path $repository ('artifacts\release-cli-' + [Guid]::NewGuid().ToString('N'))
    Invoke-Dotnet @('pack', 'src\TSqlFormatter.Cli\TSqlFormatter.Cli.csproj', '-c', 'Release', '--no-build', '--no-restore', '-o', $cliOutput)
    $cliPackages = @(Get-ChildItem -LiteralPath $cliOutput -Filter '*.nupkg')
    if ($cliPackages.Count -ne 1) { throw 'Expected exactly one CLI package.' }
    $cliPackage = $cliPackages[0]
    Copy-Item -LiteralPath $cliPackage.FullName -Destination $release
    $deliverables += [ordered]@{ file = $cliPackage.Name; kind = 'CLI-preview' }
    $toolDirectory = Join-Path $cliOutput 'tool'
    [xml]$cliProject = Get-Content -LiteralPath 'src\TSqlFormatter.Cli\TSqlFormatter.Cli.csproj'
    Invoke-Dotnet @('tool', 'install', 'TSqlFormatter.Tool', '--tool-path', $toolDirectory, '--source', $cliOutput, '--version', [string]$cliProject.Project.PropertyGroup.Version)
    $text = 'select 1;' | & (Join-Path $toolDirectory 'tsqlformat.exe') -
    if ($LASTEXITCODE -ne 0 -or ($text | Out-String).Trim() -ne 'SELECT 1;') { throw 'Packaged CLI smoke failed.' }
    foreach ($file in $deliverables) {
        $file.sha256 = (Get-FileHash -LiteralPath (Join-Path $release $file.file) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [ordered]@{ schemaVersion = 1; label = 'local-release'; files = $deliverables; note = 'Package checks and bundled-profile checks passed. Installed-host checks are recorded separately in VALIDATION.md. No publication.' } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $release 'release-manifest.json') -Encoding UTF8
    Write-Output "Release installers and profiles saved to $release"
} finally { Pop-Location }
