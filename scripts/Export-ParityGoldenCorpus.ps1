#requires -Version 7.0
param([switch] $Write)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
dotnet build (Join-Path $projectRoot 'src/TSqlFormatter.Ssms/TSqlFormatter.Ssms.csproj') --no-restore -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Golden generator build failed.' }
$assemblyDirectory = Join-Path $projectRoot 'src/TSqlFormatter.Ssms/bin/Debug/net472'
foreach ($dll in @('Microsoft.SqlServer.TransactSql.ScriptDom.dll','TSqlFormatter.Core.dll','TSqlFormatter.Configuration.dll')) {
    Add-Type -Path (Join-Path $assemblyDirectory $dll)
}
$formatter = [TSqlFormatter.Core.Formatting.ScriptDomSqlFormatter]::new()
$request = [TSqlFormatter.Core.Formatting.FormatRequest]::new()
$cases = @()
foreach ($path in @('tests/TSqlFormatter.Core.Tests/SqlCompleteParity/corpus.tsv','tests/TSqlFormatter.GoldenTests/parity-edge-corpus.tsv')) {
    $cases += Get-Content -LiteralPath (Join-Path $projectRoot $path) | Select-Object -Skip 1 | ForEach-Object {
        $cells = $_ -split "`t"
        [pscustomobject]@{ Name = $cells[0]; Sql = $cells[1].Replace('\n', "`n") }
    }
}
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("Profile`tCase`tSourceJson`tExpectedJson")
foreach ($profile in [TSqlFormatter.Configuration.NativeFormattingPresets]::Profiles) {
    foreach ($case in $cases) {
        $result = $formatter.Format($case.Sql, $profile.Options, $request, [System.Threading.CancellationToken]::None)
        if (-not $result.ParseSucceeded) { throw "Invalid golden input: $($case.Name)" }
        $second = $formatter.Format($result.Text, $profile.Options, $request, [System.Threading.CancellationToken]::None)
        if ($second.Text -cne $result.Text) { throw "Unstable golden output: $($profile.Id)/$($case.Name)" }
        $lines.Add($profile.Id + "`t" + $case.Name + "`t" + [Newtonsoft.Json.JsonConvert]::SerializeObject($case.Sql) + "`t" +
            [Newtonsoft.Json.JsonConvert]::SerializeObject($result.Text))
    }
}
$content = ($lines -join "`n") + "`n"
$outputPath = Join-Path $projectRoot 'tests/TSqlFormatter.GoldenTests/parity-golden.tsv'
if ($Write) { [System.IO.File]::WriteAllText($outputPath, $content, [System.Text.UTF8Encoding]::new($false)) }
elseif (-not (Test-Path -LiteralPath $outputPath) -or [System.IO.File]::ReadAllText($outputPath).Replace("`r`n", "`n") -cne $content) {
    throw 'Golden corpus changed. Review the differences before regenerating with -Write.'
}
Write-Output "Verified $($lines.Count - 1) native alternative golden cases: $outputPath"
