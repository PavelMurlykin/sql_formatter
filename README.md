# T-SQL Formatter

An extensible T-SQL formatter under active development. The current Core exposes
a ScriptDom-based parser, token helpers, a line map, a layout renderer, an
AST-to-document builder, and programmatic formatting for supported SELECT/INSERT/UPDATE/DELETE/MERGE/OUTPUT/CTE/subquery/CASE/window/set-operator/FROM/JOIN
queries. The CLI supports stdin, one file, `--write`, `--check`, configuration
discovery, and local preview `dotnet tool` packaging. Broader SQL coverage is planned in
[`CODEX_DEVELOPMENT_PLAN.md`](CODEX_DEVELOPMENT_PLAN.md).
An experimental Visual Studio VSIX provides Document, Selection, and Statement
commands for `.sql` files and discovers the nearest `.tsqlformatter.json`. Host
behavior still requires manual validation before a user-ready release.
The Release VSIX can be checked with `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Vsix.ps1`.

Current usage: [English manual](docs/user-manual.en.md) ·
[Русское руководство](docs/user-manual.ru.md).

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
```

`--check` returns 0 for an already formatted file, 1 when formatting is needed,
or 2 on an error. See the manuals for stdin and `.tsqlformatter.json` behavior.

To build and install the preview tool from this repository:

```powershell
dotnet pack src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj -c Release -o artifacts/tool
dotnet tool install TSqlFormatter.Tool --tool-path artifacts/tool-bin --source artifacts/tool --version 0.1.0-preview.2
.\artifacts\tool-bin\tsqlformat.exe query.sql --check
```

The package is not published. Inactive GitHub Actions, Azure Pipelines, and
pre-commit examples are under [`examples/ci`](examples/ci/); set their SQL
directory before enabling them. The CI examples build the tool from source.

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
formatting and configuration parsing. Add minimal crash reproducers to
`tests/TSqlFormatter.Core.Tests/CrashCorpus/`; they run with the normal suite.
See the architecture decision records in `docs/adr/`.

## Projects

- `src/TSqlFormatter.Core` — parser, token helpers, line map, and layout engine (`netstandard2.0`).
- `src/TSqlFormatter.Configuration` — JSON settings, profiles, and discovery (`netstandard2.0`).
- `src/TSqlFormatter.Cli` — command-line formatter (`net10.0`; .NET 10 runtime required).
- `tests/TSqlFormatter.Core.Tests` — Core, configuration, and CLI tests (`net10.0`).
- `tests/TSqlFormatter.GoldenTests` — golden formatting tests (`net10.0`).
- `benchmarks/TSqlFormatter.Benchmarks` — renderer microbenchmarks (`net10.0`).

Further formatting rules and CLI options are planned for later roadmap tasks. See
the manuals for the supported subset.
