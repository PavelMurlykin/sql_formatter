$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$installer = Join-Path $root 'scripts\Install-SSMS20.ps1'
# Load only the filesystem helper. The actual installer still requires a real
# SSMS 20 executable and elevation; the regression runs in a synthetic IDE.
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($installer, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$helper = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Backup-SSMS20Extension'
}, $true)
if ($null -eq $helper) { throw 'Installer backup helper is missing.' }
. ([scriptblock]::Create($helper.Extent.Text))
$testRoot = Join-Path $root ('artifacts\installer-smoke\' + [Guid]::NewGuid().ToString('N'))
$ide = Join-Path $testRoot 'IDE'
$backups = Join-Path $testRoot 'Backups'
$installed = Join-Path $ide 'Extensions\TSqlFormatter.Ssms20'
function New-ManagedPackage([string]$path, [string]$content) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    '{"id":"TSqlFormatter.Ssms20"}' | Set-Content (Join-Path $path 'tsqlformatter-install.json')
    $content | Set-Content (Join-Path $path 'TSqlFormatter.Ssms20.pkgdef')
}
New-ManagedPackage $installed 'current'
New-ManagedPackage (Join-Path $ide 'TSqlFormatter.Ssms20-backup-old') 'legacy'
Backup-SSMS20Extension $ide $backups | Out-Null
$scanned = @(Get-ChildItem $ide -Recurse -Filter '*.pkgdef')
if ($scanned.Count -ne 1 -or (Get-Content $scanned[0].FullName) -ne 'current') {
    throw 'The shell can still discover an outdated backup package.'
}
$saved = @(Get-ChildItem $backups -Recurse -Filter '*.pkgdef' | ForEach-Object { Get-Content $_.FullName })
if ($saved.Count -ne 2 -or $saved -notcontains 'legacy' -or $saved -notcontains 'current') {
    throw 'The upgrade did not preserve both old and current packages.'
}
Backup-SSMS20Extension $ide $backups | Out-Null
if (@(Get-ChildItem $ide -Recurse -Filter '*.pkgdef').Count -ne 1 -or
    @(Get-ChildItem $backups -Recurse -Filter '*.pkgdef').Count -ne 3) {
    throw 'A repeated upgrade created a discoverable backup or overwrote a previous backup.'
}
foreach ($unsafe in @($ide, (Join-Path $ide 'nested-backups'))) {
    $rejected = $false
    try { Backup-SSMS20Extension $ide $unsafe | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'An unsafe backup directory was accepted.' }
}
$unmanaged = Join-Path $ide 'TSqlFormatter.Ssms20-backup-unmanaged'
New-Item -ItemType Directory -Path $unmanaged | Out-Null
$rejected = $false
try { Backup-SSMS20Extension $ide $backups | Out-Null } catch { $rejected = $true }
if (-not $rejected -or -not (Test-Path $unmanaged)) { throw 'An unmanaged folder was moved.' }
Write-Output 'SSMS20 installer regression passed: migration, repeated upgrade, unsafe paths, unmanaged folders.'
