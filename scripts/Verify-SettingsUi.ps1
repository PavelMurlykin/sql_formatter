param([string]$RestoreSource = '', [switch]$SkipRestore)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\tests\TSqlFormatter.SettingsUiSmoke\TSqlFormatter.SettingsUiSmoke.csproj'
if ($env:OS -ne 'Windows_NT') { throw 'The WinForms settings smoke test requires Windows.' }
if (-not $SkipRestore) {
    $arguments = @('restore', $project)
    if ($RestoreSource) { $arguments += @('--source', $RestoreSource) }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Settings UI restore failed.' }
}
& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Settings UI build failed.' }
$executable = Join-Path $PSScriptRoot '..\tests\TSqlFormatter.SettingsUiSmoke\bin\Release\net472\TSqlFormatter.SettingsUiSmoke.exe'
foreach ($variant in @(@(), @('--english'), @('--dark'), @('--english', '--dark'))) {
    & $executable --verify @variant
    if ($LASTEXITCODE -ne 0) { throw 'Settings UI smoke failed.' }
}
