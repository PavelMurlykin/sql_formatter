# Local release-candidate procedure

Status: local candidate only, checked 2026-10-01, including the narrow
installed-IDE SC-27 smoke checks. No NuGet package, VSIX, or
release archive has been published. These are current package identities and
versions, not a promise of a common stable semantic-version series:

| Component | Package identity | Version | Tested host / runtime |
| --- | --- | --- | --- |
| CLI | `TSqlFormatter.Tool` | `0.1.0-preview.6` | .NET 10 Runtime; package install and stdin smoke in the local build and CI |
| Visual Studio | `TSqlFormatter.VisualStudio` | `0.2.5` | Build/package and shared control checks; installed-host checks pending. Earlier 0.2.4 smoke: Community 2026 18.10.2 x64 |
| SSMS | `TSqlFormatter.Ssms` | `0.6.4` | Build/package and shared control checks; installed-host checks pending. Earlier 0.6.3 smoke: 22.10.2 x64, host ScriptDom 18.0.105.0 |

## Build and verify

On Windows with .NET 10 SDK, Visual Studio 2026 and its extension-development
component, run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-ReleaseCandidate.ps1
```

The script restores the solution, builds Release, runs both test projects,
checks the Visual Studio VSIX, packs the CLI, copies both VSIX files, checks
their identities/targets/required assemblies, installs the CLI from the local
package into an ignored smoke directory, and formats `select 1;`. It writes
three packages and `candidate-manifest.json` with package versions and SHA-256
hashes under `artifacts/release-candidate/`. The 2026-10-01 local run uses
`artifacts/parity-final-candidate/` and 1288 tests (1143 Core and 145 golden).
The later settings-redesign run uses `artifacts/settings-ui-candidate-final/`,
1299 tests (1154 Core and 145 golden), VSIX versions 0.2.5/0.6.4 and
separate shared-control smoke coverage. It does not install the new VSIX
packages into the user's IDEs.
The script deliberately fails when the output directory is nonempty; use
`-OutputDirectory artifacts/another-candidate` for another run. It never
publishes or deletes packages. For a previously restored checkout, `-SkipRestore`
is available; do not use that shortcut to validate a clean machine. When the
build host cannot reach NuGet but already has pinned packages locally, pass
`-RestoreSource 'C:\path\to\nuget-cache'` with the path to that local
package cache. The default restore uses configured NuGet sources and
therefore needs network access if its cache is incomplete. The full default
restore was blocked by this environment's network policy; the local-source
restore and `-SkipRestore` are the checked paths here.

Generated hashes identify that particular build's files. Repeat the procedure
from the selected release commit or tag before distribution; matching hashes
across different builds are not promised. Compare a received file's
`Get-FileHash -Algorithm SHA256` output to its manifest entry.

## Install for evaluation

Install the local CLI package from the candidate directory (a .NET 10 Runtime
is required to run it):

```powershell
dotnet tool install TSqlFormatter.Tool --tool-path artifacts/eval-tool --source artifacts/release-candidate --version 0.1.0-preview.6
.\artifacts\eval-tool\tsqlformat.exe - --profile Default
```

The second command reads SQL from stdin. For an interactive file, use
`.\artifacts\eval-tool\tsqlformat.exe query.sql --check`; exit codes are
documented in the [English](user-manual.en.md) and
[Russian](user-manual.ru.md) manuals. Install the Visual Studio VSIX from the
candidate directory into Visual Studio 2026 and restart it. In a disposable
`.sql` file, use `SQL Formatter → Format T-SQL Document`, `Selection`, or `Statement`.
Install `TSqlFormatter.Ssms.vsix` into SSMS only if accepting the experimental
version-specific boundary; do not install it into Visual Studio.

## Support and owner decisions

The redesigned settings in 0.2.5/0.6.4 have a separate
[validation checklist](settings-ui-validation.md). Previous installed-host
evidence below does not certify these new package versions.

The CLI and Core are preview quality. Visual Studio's primary commands passed
the narrow smoke matrix, including native JSON import and full settings.
Format-on-save, paste, named profile save/load/export and error paths still
need live-IDE validation before being described as fully supported.
SSMS 0.6.3 is LIMITED SUPPORT only for the checked 22.10.2 host. The older
0.6.0/22.10.1 matrix is historical, not certification of the new package.

Before external distribution, the repository owner must choose the public
product/package name, distribution channels (for example, NuGet, extension
marketplace, or repository release), and whether/how to sign packages. No
choice or publication is implied by this local candidate. After those choices,
repeat the verification on the exact release commit and update the two manuals
and support matrices to match the distributed artifacts.
