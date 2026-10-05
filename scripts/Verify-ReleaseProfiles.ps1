param([string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\release'), [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = [IO.Path]::GetFullPath($ReleaseDirectory)
$project = Join-Path $repository 'tests\TSqlFormatter.ReleaseSmoke\TSqlFormatter.ReleaseSmoke.csproj'
if (-not $SkipBuild) {
    foreach ($platform in @('AnyCPU', 'x86')) {
        & dotnet build $project -c Release -p:Platform=$platform --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Profile smoke build failed: $platform" }
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$profiles = @('ADIR_SQL_Main', 'AV_Profile', 'Right-aligned-EPM-AWB2')
$baseline = @{}
$results = @()
foreach ($hostName in @('VS2022', 'VS2026', 'SSMS20', 'SSMS22')) {
    $work = Join-Path $repository ('artifacts\release-profile-smoke\' + $hostName + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $release "TSqlFormatter.$hostName.vsix"), $work)
    $harnessDirectory = if ($hostName -eq 'SSMS20') { 'bin\x86\Release\net472' } else { 'bin\Release\net472' }
    $harness = Join-Path (Split-Path $project) $harnessDirectory
    foreach ($file in @('TSqlFormatter.ReleaseSmoke.exe', 'TSqlFormatter.ReleaseSmoke.exe.config')) {
        Copy-Item -LiteralPath (Join-Path $harness $file) -Destination $work
    }
    $output = Join-Path $work 'output'
    $hostScriptDom = switch ($hostName) {
        'SSMS20' { Join-Path ${env:ProgramFiles(x86)} 'Microsoft SQL Server Management Studio 20\Common7\IDE\Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll' }
        'SSMS22' { Join-Path $env:ProgramFiles 'Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\Application\Microsoft.SqlServer.TransactSql.ScriptDom.dll' }
        default { '' }
    }
    $log = & (Join-Path $work 'TSqlFormatter.ReleaseSmoke.exe') $release $output $hostScriptDom 2>&1
    $exitCode = $LASTEXITCODE
    $log | Set-Content -LiteralPath (Join-Path $work 'verification.log') -Encoding UTF8
    $log | Write-Output
    if ($exitCode -ne 0) { throw "$hostName bundled profile verification failed. See $work" }
    foreach ($file in Get-ChildItem -LiteralPath $output -Filter '*.sql') {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($hostName -eq 'VS2022') { $baseline[$file.Name] = $hash }
        elseif ($baseline[$file.Name] -ne $hash) { throw "$hostName differs from VS2022: $($file.Name)" }
    }
    $results += [ordered]@{
        host = $hostName
        file = "TSqlFormatter.$hostName.vsix"
        sha256 = (Get-FileHash -LiteralPath (Join-Path $release "TSqlFormatter.$hostName.vsix") -Algorithm SHA256).Hash.ToLowerInvariant()
        cases = 45
        architecture = if ($hostName -eq 'SSMS20') { 'x86' } else { 'x64' }
        result = 'passed'
        identicalOutput = $true
    }
}
$report = [ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd')
    scope = 'Extracted-installer libraries: profile import, editor/store round trip, formatting, token preservation, idempotence, identical outputs. Separate from installed-IDE UI validation.'
    profiles = @($profiles | ForEach-Object { [ordered]@{ file = "$_.json"; sha256 = (Get-FileHash -LiteralPath (Join-Path $release "$_.json") -Algorithm SHA256).Hash.ToLowerInvariant() } })
    packages = $results
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $release 'profile-verification.json') -Encoding UTF8
Write-Output 'All four installer libraries produced identical output for all 45 profile/SQL combinations.'
