# T-SQL Formatter

An extensible T-SQL formatter under active development. The repository currently
contains the Phase 0 solution scaffold and ScriptDom dependency described in
[`CODEX_DEVELOPMENT_PLAN.md`](CODEX_DEVELOPMENT_PLAN.md).

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

## Projects

- `src/TSqlFormatter.Core` — formatter engine boundary (`netstandard2.0`).
- `src/TSqlFormatter.Configuration` — configuration boundary (`netstandard2.0`).
- `src/TSqlFormatter.Cli` — command-line host (`net8.0`).
- `tests/TSqlFormatter.Core.Tests` — Core unit tests (`net8.0`).
- `tests/TSqlFormatter.GoldenTests` — golden formatting tests (`net8.0`).

Parser, formatting rules, configuration loading, and CLI commands are added in
later roadmap tasks.
