# Local release packaging

All distributable installers and formatting profiles are saved in repository-root
`release/`, following [AGENTS.md](../AGENTS.md). Build intermediates and logs stay
in `artifacts/` or `bin/obj`.

## Build and verify

Use Windows, .NET 10 SDK, Visual Studio extension-development build tools and the
pinned NuGet dependencies. Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1
```

When NuGet is unavailable and the packages are cached:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -RestoreSource C:\Users\PAVEL\.nuget\packages
```

`Build-ReleaseCandidate.ps1` remains an alias for the same procedure. Its optional
`-OutputDirectory` accepts only root `release/`, including its absolute path.
Rerunning refreshes the deliverables. `-SkipRestore` requires previously restored
solution, release-smoke and settings-UI projects.

The builder performs a Release solution build and tests, checks the manifests and
required assets in each VSIX, copies the three canonical JSON profiles, tests
their import/store/formatting against extracted package libraries, runs the
WinForms settings scenario, and packs/installs/smoke-tests the local CLI. It writes
`release-manifest.json` with deliverable versions and SHA-256 values and
`profile-verification.json` with profile results. It does not install IDE
extensions or publish packages. Installation and UI checks are recorded
separately in [release validation](../release/VALIDATION.md).

## Deliverables

| Host | Package | Adapter version |
| --- | --- | --- |
| VS 2022 17.14 x64 | TSqlFormatter.VS2022.vsix | 0.2.9 |
| VS 2026 18.x x64 | TSqlFormatter.VS2026.vsix | 0.2.9 |
| SSMS 20 x86 | TSqlFormatter.SSMS20.vsix + Install-SSMS20.ps1 | 0.6.8 |
| SSMS 22 x64 | TSqlFormatter.SSMS22.vsix | 0.6.8 |
| CLI, .NET 10 Runtime | TSqlFormatter.Tool.0.1.0-preview.6.nupkg | 0.1.0-preview.6 |

Profiles: `ADIR_SQL_Main.json`, `AV_Profile.json`, `Right-aligned-EPM-AWB2.json`.
The conversion limitations remain documented in the manuals and profile reports.

Use the [Russian](../release/README.ru.md) or [English](../release/README.en.md)
installation guide. SSMS 20 requires the supplied Administrator PowerShell script
because the standard VSIX Installer does not recognize its isolated legacy
shell. VS 2022 and VS 2026 share an extension identity but have disjoint manifest
version ranges. SSMS 20 has a separate identity and SDK 15 adapter; SSMS 22 uses
the modern SDK. The shared formatter and settings are bundled in all packages.

Install the additional local CLI without publishing:

```powershell
dotnet tool install TSqlFormatter.Tool --tool-path artifacts/eval-tool --source release --version 0.1.0-preview.6
.\artifacts\eval-tool\tsqlformat.exe query.sql --check
```

These are unsigned local preview packages. Existing historical IDE matrices
describe only their stated versions; they do not certify the new four packages
or every minor host version. No stable public release is claimed.
