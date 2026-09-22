# 0001 — Use ScriptDom for T-SQL parsing

Status: Accepted

Date: 2026-09-22

## Context

Formatting requires T-SQL syntax structure, token boundaries and source positions. In particular, comments and literals must survive unchanged, and the formatter must reject invalid input safely. Microsoft ScriptDom exposes an AST and a [token stream with source positions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom.tsqlparsertoken). The selected [NuGet package](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom/180.107.0) supports the Core project's `netstandard2.0` target.

## Decision

Use `Microsoft.SqlServer.TransactSql.ScriptDom` in Core for tokenization, parsing, fragment boundaries and version-specific syntax. Keep original source text and tokens available to later formatting stages. Parser errors must become formatter diagnostics; they must not be ignored. Do not use the ScriptDom SQL generator as the main formatter: layout is governed by [ADR 0002](0002-custom-layout-engine.md).

The package is referenced by Core, and its version is pinned in `Directory.Packages.props`. The parser abstraction and dialect mapping belong to Phase 1.

## Consequences

- Parsing can be tested independently of rendering and without a SQL Server connection.
- Token and AST information must stay linked so that comments, literals and source spans can be preserved.
- ScriptDom's supported grammar and package upgrades need regression tests across the dialects the project claims to support.

## Alternatives

- A custom T-SQL parser would duplicate a large, evolving grammar.
- Regex or token-only formatting would not provide reliable syntax structure.
- ScriptDom's generator alone would not provide the planned layout and profile controls.
