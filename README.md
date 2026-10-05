# T-SQL Formatter

An extensible T-SQL formatter under active development. The current Core exposes
a ScriptDom-based parser, token helpers, a line map, a layout renderer, an
AST-to-document builder, and programmatic formatting for supported SELECT/INSERT/UPDATE/DELETE/MERGE/OUTPUT/CTE/subquery/CASE/window/set-operator/FROM/JOIN
queries. The CLI supports stdin or one file to stdout, recursive directories and
multiple files with `--write`/`--check`, configuration discovery, and local preview
`dotnet tool` packaging. Broader SQL coverage is planned in
[`CODEX_DEVELOPMENT_PLAN.md`](CODEX_DEVELOPMENT_PLAN.md).
An experimental Visual Studio VSIX provides Document, Selection, and Statement
commands for `.sql` files and discovers the nearest `.tsqlformatter.json`. These
primary commands passed limited installed-IDE smoke tests. Separate installers
target Visual Studio 2022/2026 and SSMS 20/22; see the current release validation
for the exact checks and remaining gaps.
The Release VSIX can be checked with `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Vsix.ps1`.

Current usage: [English manual](docs/user-manual.en.md) ·
[Русское руководство](docs/user-manual.ru.md).
Local packaging and support boundaries: [release-candidate guide](docs/release-candidate.md) ·
[changelog](CHANGELOG.md).

Installers and converted SQL Prompt / SQL Complete profiles are delivered in
[`release/`](release/), as required by [AGENTS.md](AGENTS.md).
Use the [installation guide](release/README.en.md) and
[validation results](release/VALIDATION.md).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1
```

This builds and checks four VSIX packages, all three profiles, the settings UI,
and the locally packaged CLI. Rerunning refreshes the same release directory.

## Prerequisites

- .NET 10 SDK (the repository accepts newer 10.0 feature bands)
- For the VSIX project and the full solution build: Windows and Visual Studio 2026 with the Visual Studio extension development component

## Build

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
```

## CLI

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql --write
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql --check
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- ./sql --check
```

`--check` returns 0 for an already formatted file, 1 when formatting is needed,
or 2 on an error. See the manuals for stdin and `.tsqlformatter.json` behavior.

To build and install the preview tool from this repository:

```powershell
dotnet pack src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj -c Release -o release
dotnet tool install TSqlFormatter.Tool --tool-path artifacts/tool-bin --source release --version 0.1.0-preview.6
.\artifacts\tool-bin\tsqlformat.exe query.sql --check
```

The package is not published. The active [GitHub Actions workflow](.github/workflows/ci.yml)
builds and tests Core/CLI on Linux and Windows, verifies the VSIX package on Windows,
and smoke-tests the locally packed CLI tool. It does not check a user SQL directory.
Optional GitHub Actions, Azure Pipelines, and pre-commit examples are under
[`examples/ci`](examples/ci/); set their SQL directory before enabling them.

## Test

```powershell
dotnet test TSqlFormatter.sln
```

The shared C# baseline is defined in `Directory.Build.props` and `.editorconfig`:
nullable reference types and implicit usings are enabled, and warnings in project
code fail the build.

Package versions, including `Microsoft.SqlServer.TransactSql.ScriptDom`, are
pinned in `Directory.Packages.props`.

The test suite includes real CLI-process integration checks for stdout, exit codes,
file writing, and configuration discovery, plus seeded property/fuzz checks for
formatting and configuration parsing. The representative `ValidCorpus` and malformed
`CrashCorpus` cases run with the normal suite. Add minimal crash reproducers to
`tests/TSqlFormatter.Core.Tests/CrashCorpus/`.
The [large-procedure baseline](docs/performance/large-procedure-baseline.md) is a
separate reproducible, non-gating smoke measurement.
See the architecture decision records in `docs/adr/`.

## Projects

- `src/TSqlFormatter.Core` — parser, token helpers, line map, and layout engine (`netstandard2.0`).
- `src/TSqlFormatter.Configuration` — JSON settings, profiles, and discovery (`netstandard2.0`).
- `src/TSqlFormatter.Cli` — command-line formatter (`net10.0`; .NET 10 runtime required).
- `src/TSqlFormatter.VisualStudio2022` / `src/TSqlFormatter.VisualStudio` — VS 2022 17.14 / VS 2026 18.x adapters (`net472`).
- `src/TSqlFormatter.Ssms20` / `src/TSqlFormatter.Ssms` — SSMS 20 x86 / SSMS 22 x64 adapters (`net472`).
- `tests/TSqlFormatter.Core.Tests` — Core, configuration, and CLI tests (`net10.0`).
- `tests/TSqlFormatter.GoldenTests` — golden formatting tests (`net10.0`).
- `benchmarks/TSqlFormatter.Benchmarks` — renderer microbenchmarks (`net10.0`).

Further formatting rules and CLI options are planned for later roadmap tasks. See
the manuals for the supported subset.

SQL Complete working profiles AV_Profile and Right-aligned-EPM-AWB2 are available as
native import JSON for VSIX 0.2.8 / SSMS 0.6.7. Their source mapping, explicit numeric-mode
approximations and audit against both EPM-RAC database projects are described in
[the working-profile report](docs/sql-complete-working-profiles.md).
The current release includes these profiles and ADIR_SQL_Main in `release/`, with
Visual Studio packages 0.2.9 and SSMS packages 0.6.8.
