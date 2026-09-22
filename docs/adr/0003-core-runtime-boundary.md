# 0003 — Keep Core compatible with .NET Standard 2.0

Status: Accepted

Date: 2026-09-22

## Context

Core must run independently of Visual Studio and SSMS. Future in-process editor hosts may require .NET Framework compatibility, while the CLI targets .NET 8. Microsoft [recommends `netstandard2.0` for libraries that need broad compatibility](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/cross-platform-targeting), and the current [ScriptDom package](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom/180.107.0) provides a `netstandard2.0` asset.

## Decision

Target `netstandard2.0` in `TSqlFormatter.Core` and `TSqlFormatter.Configuration`; target `net8.0` in the CLI and test projects. Keep IDE APIs, editor state, UI threading and host-specific logging outside Core. Configuration may depend on Core, and hosts may depend on both; Core must never depend on a host.

Do not raise Core's minimum runtime without a new ADR. Add a modern .NET target later only if a measured need warrants multi-targeting. The Visual Studio and SSMS adapters will choose their exact targets during their respective compatibility spikes.

## Consequences

- Core can be built and tested without either IDE installed.
- Core APIs are limited to .NET Standard 2.0 or must be isolated behind later target-specific builds.
- `netstandard2.0` is a compatibility boundary, not proof that an editor extension will load; each host still needs integration testing. For .NET Framework consumers, [4.7.2 or newer is recommended](https://learn.microsoft.com/en-us/dotnet/standard/net-standard).

## Alternatives

- `net8.0` alone simplifies Core development but blocks potential in-process .NET Framework hosts.
- A .NET Framework-only Core would constrain the standalone CLI and cross-platform use.
- Multi-targeting immediately adds build and test paths before a concrete host requirement exists.
