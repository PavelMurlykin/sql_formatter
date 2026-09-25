# T-SQL Formatter

An extensible T-SQL formatter under active development. The current Core exposes
a ScriptDom-based parser, token helpers, a line map, a layout renderer, an
AST-to-document builder, and programmatic formatting for supported SELECT/INSERT/UPDATE/DELETE/MERGE/OUTPUT/CTE/subquery/CASE/window/set-operator/FROM/JOIN
queries. The CLI supports stdin, one file, `--write`, `--check`, and configuration
discovery. Broader SQL coverage is planned in
[`CODEX_DEVELOPMENT_PLAN.md`](CODEX_DEVELOPMENT_PLAN.md).

Current usage: [English manual](docs/user-manual.en.md) ·
[Русское руководство](docs/user-manual.ru.md).

## Prerequisites

- .NET SDK 8.0 or newer

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
file writing, and configuration discovery. See the architecture decision records
in `docs/adr/`.

## Projects

- `src/TSqlFormatter.Core` — parser, token helpers, line map, and layout engine (`netstandard2.0`).
- `src/TSqlFormatter.Configuration` — JSON settings, profiles, and discovery (`netstandard2.0`).
- `src/TSqlFormatter.Cli` — command-line formatter (`net8.0`).
- `tests/TSqlFormatter.Core.Tests` — Core, configuration, and CLI tests (`net8.0`).
- `tests/TSqlFormatter.GoldenTests` — golden formatting tests (`net8.0`).
- `benchmarks/TSqlFormatter.Benchmarks` — renderer microbenchmarks (`net8.0`).

Further formatting rules and CLI options are planned for later roadmap tasks. See
the manuals for the supported subset.
