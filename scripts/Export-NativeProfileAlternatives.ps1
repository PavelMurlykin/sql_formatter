#requires -Version 7.0
param([switch] $Write)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$configurationProject = Join-Path $projectRoot 'src/TSqlFormatter.Configuration/TSqlFormatter.Configuration.csproj'
dotnet build $configurationProject --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Configuration build failed.' }

# PowerShell 7 supplies Newtonsoft.Json; load only our netstandard assemblies.
Add-Type -Path (Join-Path $projectRoot 'src/TSqlFormatter.Core/bin/Debug/netstandard2.0/TSqlFormatter.Core.dll')
Add-Type -Path (Join-Path $projectRoot 'src/TSqlFormatter.Configuration/bin/Debug/netstandard2.0/TSqlFormatter.Configuration.dll')
$serializer = [TSqlFormatter.Configuration.SqlFormatterConfigurationSerializer]::new($null)
$outputDirectory = Join-Path $projectRoot 'examples/profiles'
$inventoryDirectory = Join-Path $projectRoot 'tests/TSqlFormatter.Core.Tests/SqlCompleteParity'
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Sync-GeneratedFile([string] $path, [string] $content) {
    if ($Write) {
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
        [System.IO.File]::WriteAllText($path, $content, $utf8)
    }
    elseif (-not [System.IO.File]::Exists($path) -or
        [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n") -cne $content.Replace("`r`n", "`n")) {
        throw "Generated artifact is stale: $path. Run with -Write to regenerate."
    }
    Write-Output "Verified: $path"
}

foreach ($profile in [TSqlFormatter.Configuration.NativeFormattingPresets]::Profiles) {
    Sync-GeneratedFile (Join-Path $outputDirectory ($profile.Id + '.json')) ($serializer.SerializeV2($profile.Options))
}
$snapshot = [System.IO.File]::ReadAllBytes((Join-Path $inventoryDirectory 'profile-values.tsv'))
$coverage = [System.IO.File]::ReadAllText((Join-Path $inventoryDirectory 'coverage.tsv'))
$report = [TSqlFormatter.Configuration.ProfileCorrespondenceReport]::Create($snapshot, $coverage)
Sync-GeneratedFile (Join-Path $outputDirectory 'sql-complete-correspondence.tsv') $report
