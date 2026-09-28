# 0007 — Use .NET 10 LTS for the standalone tool

Status: Accepted

Date: 2026-09-28

## Context

The CLI preview package, tests, and benchmarks targeted .NET 8. Its support ends on 2026-11-10, while .NET 10 LTS is supported through 2028-11-14 according to the [official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy). The standalone tool should not require a runtime approaching end of support. The IDE adapters still need their .NET Framework compatibility boundary.

## Decision

Target `net10.0` in the CLI, test projects, and benchmarks. Require a .NET 10 SDK through `global.json`, allowing newer .NET 10 feature bands. Keep Core and Configuration on `netstandard2.0` and the Visual Studio and SSMS adapters on `net472`. Advance the unpublished preview tool package to `0.1.0-preview.2` because its runtime requirement changes.

This supersedes only the CLI/test target choice in [ADR 0003](0003-core-runtime-boundary.md); the Core compatibility decision remains in force.

## Consequences

- Building this repository requires a .NET 10 SDK; running the CLI requires a .NET 10 runtime.
- Existing .NET 8-only tool installations must upgrade their runtime and reinstall the new preview package.
- No formatting behavior, configuration schema, or IDE target framework changes in this stage.
