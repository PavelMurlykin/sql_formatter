# Changelog

This repository has no published stable release. The entries below describe
locally built preview packages; version numbers belong to separate adapters.

## Local parity candidate (SC-27) — 2026-10-01

- All 977 SQL Complete inventory paths resolved: 969 configurable native
  mappings and 8 user-approved T-SQL exclusions; zero unresolved entries.
  This is capability coverage, not byte-identical SQL Complete output.
- Final executable mapping/evidence audit, 82 new golden cases for the two
  original native alternatives, v1 regression checks and a reproducible
  time/allocated-memory guard. 1143 Core + 145 golden tests pass.
- Visual Studio VSIX 0.2.4 / SSMS VSIX 0.6.3: full settings split into
  Settings/Profiles/Preview tabs, and native preview displays LF results
  correctly without changing configured formatter line endings.
- Narrow installed-package smoke passed on VS Community 2026 18.10.2 and
  SSMS 22.10.2 x64; SSMS remains experimental LIMITED SUPPORT. CLI remains
  0.1.0-preview.6. Both user manuals and support matrices updated.
- Local candidate build/package/installed-CLI checks passed. No stable
  release, package publication or signing decision is implied.

See the [parity audit](docs/sql-complete-parity-audit.md) for exact evidence,
native-profile differences and remaining runtime limitations.

## Local release candidate — 2026-09-29

- CLI tool `TSqlFormatter.Tool` `0.1.0-preview.6`: stdin, one-file stdout,
  multi-file/directory `--check` and `--write`, `--profile`, and per-file
  `.editorconfig`/`.tsqlformatter.json` resolution.
- Visual Studio VSIX `0.2.1`: Document, Selection, Statement, option pages,
  profile import/export, opt-in formatted paste, and opt-in format-on-save.
  Diagnostic probe menu commands were removed. The three primary commands,
  Undo, save, options casing, and two open files passed an installed-IDE smoke
  test in Visual Studio Community 2026 18.10.2.
- SSMS VSIX `0.6.0`: experimental Document, Selection, Statement, and General
  options. A limited smoke matrix passed in SSMS 22.10.1 x64; other 22.x
  versions are not certified.
- Core and Configuration: supported T-SQL formatting and strict configuration
  resolution remain shared by all adapters. The regular suite has 423 Core
  tests and 62 golden tests.

See the [release-candidate guide](docs/release-candidate.md),
[Visual Studio smoke matrix](docs/visual-studio-smoke.md), and
[SSMS compatibility boundary](docs/ssms22-compatibility.md) before use.
