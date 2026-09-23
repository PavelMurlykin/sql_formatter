# T-SQL Formatter

An extensible T-SQL formatter under active development. The current Core exposes
a ScriptDom-based parser, token helpers, a line map, a layout renderer, an
AST-to-document builder, and programmatic formatting for basic SELECT/CTE/subquery/CASE/window/set-operator/FROM/JOIN
queries. CLI commands and broader SQL coverage are planned in
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

## Test

```powershell
dotnet test TSqlFormatter.sln
```

The shared C# baseline is defined in `Directory.Build.props` and `.editorconfig`:
nullable reference types and implicit usings are enabled, and warnings in project
code fail the build.

Package versions, including `Microsoft.SqlServer.TransactSql.ScriptDom`, are
pinned in `Directory.Packages.props`.

See the five architecture decision records in `docs/adr/`.

## Projects

- `src/TSqlFormatter.Core` — parser, token helpers, line map, and layout engine (`netstandard2.0`).
- `src/TSqlFormatter.Configuration` — configuration boundary (`netstandard2.0`).
- `src/TSqlFormatter.Cli` — command-line host (`net8.0`).
- `tests/TSqlFormatter.Core.Tests` — Core unit tests (`net8.0`).
- `tests/TSqlFormatter.GoldenTests` — golden formatting tests (`net8.0`).
- `benchmarks/TSqlFormatter.Benchmarks` — renderer microbenchmarks (`net8.0`).

Broader SQL formatting, configuration loading, and CLI commands are planned for
later roadmap tasks. See the manuals for the supported subset.
