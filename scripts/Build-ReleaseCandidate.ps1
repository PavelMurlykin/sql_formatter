param([string]$OutputDirectory = 'release', [string]$RestoreSource = '', [switch]$SkipRestore)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
}
if ($output.TrimEnd('\') -ne (Join-Path $repository 'release')) {
    throw 'Project delivery rule: installers and profiles must be saved in the repository-root release directory.'
}
& (Join-Path $PSScriptRoot 'Build-Release.ps1') -RestoreSource $RestoreSource -SkipRestore:$SkipRestore
