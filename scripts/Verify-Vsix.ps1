param(
    [string]$VsixPath = (Join-Path $PSScriptRoot '..\src\TSqlFormatter.VisualStudio\bin\Release\net472\TSqlFormatter.VisualStudio.vsix')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packagePath = [System.IO.Path]::GetFullPath($VsixPath)
if (-not [System.IO.File]::Exists($packagePath)) {
    throw "VSIX not found: $packagePath. Build the VSIX project in Release first."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
    $required = @(
        'extension.vsixmanifest',
        'TSqlFormatter.VisualStudio.dll',
        'TSqlFormatter.VisualStudio.pkgdef',
        'TSqlFormatter.Core.dll',
        'TSqlFormatter.Configuration.dll',
        'Microsoft.SqlServer.TransactSql.ScriptDom.dll',
        'Newtonsoft.Json.dll'
    )
    foreach ($name in $required) {
        if ($names -notcontains $name) {
            throw "VSIX is missing required asset: $name"
        }
    }

    $entry = $archive.GetEntry('extension.vsixmanifest')
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() }
    finally { $reader.Dispose() }

    $namespace = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespace.AddNamespace('vs', 'http://schemas.microsoft.com/developer/vsx-schema/2011')
    $identity = $manifest.SelectSingleNode('/vs:PackageManifest/vs:Metadata/vs:Identity', $namespace)
    $target = $manifest.SelectSingleNode('/vs:PackageManifest/vs:Installation/vs:InstallationTarget', $namespace)
    $asset = $manifest.SelectSingleNode('/vs:PackageManifest/vs:Assets/vs:Asset[@Type="Microsoft.VisualStudio.VsPackage"]', $namespace)
    if ($null -eq $identity -or $identity.Id -ne 'TSqlFormatter.VisualStudio' -or
        $null -eq $target -or $target.Id -ne 'Microsoft.VisualStudio.Community' -or
        $target.Version -ne '[17.0,)' -or $target.ProductArchitecture -ne 'amd64' -or
        $null -eq $asset) {
        throw 'VSIX manifest identity, installation target, or package asset is invalid.'
    }

    Write-Output "VSIX package verified: $packagePath ($($identity.Version))"
}
finally {
    $archive.Dispose()
}
