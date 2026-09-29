param(
    [Parameter(Mandatory = $true)] [string] $AvProfile,
    [Parameter(Mandatory = $true)] [string] $EpmProfile,
    [string] $Inventory = (Join-Path $PSScriptRoot '..\tests\TSqlFormatter.Core.Tests\SqlCompleteParity\profile-values.tsv'),
    [string] $Coverage = (Join-Path $PSScriptRoot '..\tests\TSqlFormatter.Core.Tests\SqlCompleteParity\coverage.tsv'),
    [switch] $Write
)

$ErrorActionPreference = 'Stop'

$expectedHashes = @(
    'B8EDF77E135AD48D5206C84594B448A30D5083C9D07637985707FB9AEF88F493',
    'B62A7A7BBC7994E827217DAAB440C65FB4BDAAF994CFD2371A5CCA31C01AE5EE'
)

function Get-Stage([string] $option) {
    $category = ($option -split '_')[0]
    switch ($category) {
        'TextCase' { return 'SC-04' }
        'Spacing' { return 'SC-05' }
        { $_ -in @('StackedList', 'Misc') } { return 'SC-06' }
        'Select' {
            if ($option -match '^Select_(SingleLine|SelectList)_') { return 'SC-07' }
            if ($option -match '^Select_(Into|From)_') { return 'SC-08' }
            if ($option -match '^Select_(Where|GroupBy|Having|OrderBy)_') { return 'SC-09' }
            return 'SC-10'
        }
        'Subquery' {
            if ($option -match '^Subquery_(UseSameFormattingAsInSelect|SingleLine_|SelectList_|IndentSubquery|LineBreak.*Brace)') {
                return 'SC-11'
            }
            return 'SC-12'
        }
        { $_ -in @('UnionExceptIntersect', 'Case') } { return 'SC-13' }
        'Insert' { return 'SC-14' }
        { $_ -in @('Update', 'Delete') } { return 'SC-15' }
        'Merge' {
            if ($option -match '^Merge_(Into|Using|On)_') { return 'SC-16' }
            if ($option -match '^Merge_(When|Then)_') { return 'SC-17' }
            return 'SC-18'
        }
        'Declare' { return 'SC-19' }
        'Code' { return 'SC-20' }
        { $_ -in @('ProcedureFunction', 'View') } { return 'SC-21' }
        'CreateTable' { return 'SC-22' }
        'Trigger' { return 'SC-23' }
        { $_ -in @('Execute', 'Labels') } { return 'SC-24' }
        default { throw "No planned stage for $option" }
    }
}

function Read-Profile([string] $path, [string] $expectedHash) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
    if ($hash -cne $expectedHash) { throw "Unexpected SHA-256 for $resolved`: $hash" }

    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($resolved, $settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }

    if ($document.DocumentElement.LocalName -cne 'FormatProfile' -or
        $document.DocumentElement.Attributes.Count -ne 0 -or
        $document.DocumentElement.ChildNodes.Count -ne 1 -or
        $document.DocumentElement.FirstChild.LocalName -cne 'FormatOptions') {
        throw "Unexpected profile root in $resolved"
    }

    $options = $document.DocumentElement.FirstChild
    if ($options.Attributes.Count -ne 0) { throw "Unexpected FormatOptions attributes in $resolved" }
    $leaves = @{}
    $topLevel = @{}
    $bundleCount = 0
    foreach ($option in $options.ChildNodes) {
        if ($option.NodeType -ne [System.Xml.XmlNodeType]::Element -or
            $option.LocalName -cnotin @('PropertyValue', 'SubOptions') -or
            $option.Attributes.Count -ne 1) { throw "Unexpected option node in $resolved" }
        $name = $option.GetAttribute('Name')
        if ($name -cnotmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $topLevel.ContainsKey($name)) {
            throw "Invalid or duplicate option name: $name"
        }
        $topLevel[$name] = $option.LocalName
        if ($option.LocalName -ceq 'PropertyValue') {
            if ($option.SelectNodes('./*').Count -ne 0) { throw "Nested simple option: $name" }
            $leaves[$name] = @{ Kind = 'PropertyValue'; Value = $option.InnerText.Trim() }
        }
        else {
            $bundleCount++
            $members = @{}
            foreach ($member in $option.ChildNodes) {
                if ($member.NodeType -ne [System.Xml.XmlNodeType]::Element -or
                    $member.LocalName -cne 'PropertyValue' -or $member.Attributes.Count -ne 1 -or
                    $member.SelectNodes('./*').Count -ne 0) {
                    throw "Unexpected member in $name"
                }
                $memberName = $member.GetAttribute('Name')
                if ($memberName -cnotmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $members.ContainsKey($memberName)) {
                    throw "Invalid or duplicate member in $name`: $memberName"
                }
                $members[$memberName] = $true
                $leaves["$name.$memberName"] = @{ Kind = 'SubOptions'; Value = $member.InnerText.Trim() }
            }
            if ($members.Count -eq 0) { throw "Empty SubOptions: $name" }
        }
    }
    foreach ($leaf in $leaves.Values) {
        if ($leaf.Value -cnotmatch '^(true|false|-?[0-9]+)$') {
            throw "Unexpected value in $resolved`: $($leaf.Value)"
        }
    }
    return @{ Leaves = $leaves; TopLevel = $topLevel; Bundles = $bundleCount }
}

$av = Read-Profile $AvProfile $expectedHashes[0]
$epm = Read-Profile $EpmProfile $expectedHashes[1]
if ($av.TopLevel.Count -ne 577 -or $epm.TopLevel.Count -ne 577 -or
    $av.Bundles -ne 203 -or $epm.Bundles -ne 203 -or
    $av.Leaves.Count -ne 972 -or $epm.Leaves.Count -ne 977) {
    throw 'Profile shape differs from the audited baseline.'
}
foreach ($name in $av.TopLevel.Keys) {
    if (-not $epm.TopLevel.ContainsKey($name) -or $av.TopLevel[$name] -cne $epm.TopLevel[$name]) {
        throw "Different top-level option kinds: $name"
    }
}

$all = @{}
foreach ($key in $av.Leaves.Keys) { $all[$key] = $true }
foreach ($key in $epm.Leaves.Keys) { $all[$key] = $true }
if ($all.Count -ne 977) { throw "Expected 977 union leaf paths, found $($all.Count)." }
[string[]] $keys = @($all.Keys)
[Array]::Sort($keys, [StringComparer]::Ordinal)

$changed = 0
$lines = New-Object 'System.Collections.Generic.List[string]'
$lines.Add('Path' + "`t" + 'Kind' + "`t" + 'Type' + "`t" + 'AV' + "`t" + 'EPM')
$coverageLines = New-Object 'System.Collections.Generic.List[string]'
$coverageLines.Add('Path' + "`t" + 'Stage' + "`t" + 'Status' + "`t" + 'NativeSetting' + "`t" + 'Evidence' + "`t" + 'Semantics')
foreach ($key in $keys) {
    $avLeaf = $av.Leaves[$key]
    $epmLeaf = $epm.Leaves[$key]
    $leaf = if ($null -ne $avLeaf) { $avLeaf } else { $epmLeaf }
    if ($null -ne $avLeaf -and $null -ne $epmLeaf) {
        if ($avLeaf.Kind -cne $epmLeaf.Kind) { throw "Different node kinds: $key" }
        if ($avLeaf.Value -cne $epmLeaf.Value) { $changed++ }
    }
    $valueType = if ($leaf.Value -cmatch '^(true|false)$') { 'Boolean' } else { 'Integer' }
    foreach ($candidate in @($avLeaf, $epmLeaf)) {
        if ($null -ne $candidate -and $valueType -ceq 'Boolean' -and $candidate.Value -cnotmatch '^(true|false)$') {
            throw "Different value types: $key"
        }
        if ($null -ne $candidate -and $valueType -ceq 'Integer' -and $candidate.Value -cnotmatch '^-?[0-9]+$') {
            throw "Different value types: $key"
        }
    }
    $option = ($key -split '\.')[0]
    $fields = @($key, $leaf.Kind, $valueType,
        $(if ($null -ne $avLeaf) { $avLeaf.Value } else { '-' }),
        $(if ($null -ne $epmLeaf) { $epmLeaf.Value } else { '-' }))
    $lines.Add(($fields -join "`t"))
    $coverageLines.Add((@($key, (Get-Stage $option), 'pending_semantics', '-', '-', '-') -join "`t"))
}
if ($changed -ne 190) { throw "Expected 190 different shared values, found $changed." }
$content = ($lines -join "`n") + "`n"
$destination = [System.IO.Path]::GetFullPath($Inventory)
$coverageDestination = [System.IO.Path]::GetFullPath($Coverage)
if ($Write) {
    if ([System.IO.File]::Exists($coverageDestination)) {
        throw "Coverage ledger already exists; refusing to reset its progress: $coverageDestination"
    }
    [System.IO.File]::WriteAllText($destination, $content, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($coverageDestination,
        (($coverageLines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Output "Wrote $($keys.Count) paths to $destination and $coverageDestination"
}
else {
    if (-not [System.IO.File]::Exists($destination)) { throw "Inventory not found: $destination" }
    $existing = [System.IO.File]::ReadAllText($destination, [System.Text.Encoding]::UTF8)
    if ($existing -cne $content) { throw 'Inventory differs from the supplied XML profiles.' }
    if (-not [System.IO.File]::Exists($coverageDestination)) {
        throw "Coverage ledger not found: $coverageDestination"
    }
    $coverageRows = [System.IO.File]::ReadAllLines($coverageDestination, [System.Text.Encoding]::UTF8)
    if ($coverageRows.Length -ne ($keys.Count + 1) -or $coverageRows[0] -cne $coverageLines[0]) {
        throw 'Coverage ledger has the wrong header or row count.'
    }
    for ($index = 0; $index -lt $keys.Count; $index++) {
        $fields = $coverageRows[$index + 1].Split("`t")
        if ($fields.Length -ne 6 -or $fields[0] -cne $keys[$index] -or
            $fields[1] -cne (Get-Stage (($keys[$index] -split '\.')[0]))) {
            throw "Coverage ledger has a missing, duplicate or misassigned path at row $($index + 2)."
        }
    }
    Write-Output "Verified $($keys.Count) paths, 577 options, 203 bundles and 190 shared differences."
}
