# 0005 — Treat SSMS integration as best effort

Status: Accepted, implementation conditional on a spike

Date: 2026-09-22

## Context

The product aims to offer formatting in SQL Server Management Studio, but Microsoft states that [third-party SSMS extensions are unsupported](https://learn.microsoft.com/en-us/ssms/faq). SSMS does not actively block their loading, yet that does not establish a stable extension contract.

## Decision

Keep SSMS integration experimental and isolated in a thin `TSqlFormatter.Ssms` adapter. Before implementation, run a version-specific spike covering extension loading, the current query buffer, command execution, text replacement, selection, caret, Undo, multiple windows, restart and removal. Record a compatibility matrix and decide `GO`, `LIMITED SUPPORT` or `NO-GO` for each supported version. Do not put SSMS APIs or workarounds in Core.

If a version is unsupported or the adapter breaks, Core, CLI and Visual Studio development continue independently. Extract shared editor integration code only after real duplication appears.

## Consequences

- SSMS support cannot be promised for every version; release notes must name tested versions and known issues.
- Maintenance may require version-specific fixes or a no-go decision.
- The formatter remains useful through CLI and Visual Studio even if SSMS integration proves infeasible.

## Alternatives

- Treating SSMS as a fully supported extension platform would promise a stability Microsoft does not provide.
- Omitting SSMS entirely remains an option if the spike fails, but the isolated adapter keeps a bounded experiment possible.
