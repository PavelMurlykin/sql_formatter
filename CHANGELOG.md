# Changelog

This repository has no published stable release. The entries below describe
locally built preview packages; version numbers belong to separate adapters.

## SQL Prompt ADIR_SQL_Main profile — 2026-10-02

- Visual Studio VSIX 0.2.6 / SSMS VSIX 0.6.5: importable native ADIR_SQL_Main JSON v2
  mapped from the supplied SQL Prompt exports and 16 screenshots; width 160, four
  spaces, leading commas and shared nested layout policies.
- Added optional compactness thresholds, conditional list/function/IN wrapping,
  blank-line counts, declaration/comment alignment, long SET and RESTORE MOVE/TO
  controls, independent global-variable casing and comparison/type/semicolon spacing.
  All new settings have Russian and English editor labels.
- Token-based shared layout avoids accumulating indentation on repeated formatting.
  SQL Prompt off/on regions are preserved; built-in type casing no longer changes
  schemas of user-defined types.
- Read-only audit of 441 existing procedures changed in Git during 2026: 63 exact
  layout matches, 374 stable differences, four preserved multiline-token files;
  zero parse/token/idempotence failures. Identical SQL Prompt output is not claimed.
- Both user manuals, mapping notes and reproducible audit documentation updated.
  Release builds, 1181 Core + 145 golden tests and settings UI smoke passed.

## Human-readable formatting settings — 2026-10-01

- Visual Studio VSIX 0.2.5 / SSMS VSIX 0.6.4: separate top-level SQL Formatter
  menu with formatting settings and saved profiles commands.
- Resizable shared settings window with a Russian category tree, dynamic
  search, human-labeled controls for all 1339 scalar fields, and grouped
  compound rules. Technical IDs remain available for JSON/search.
- Editable contextual SQL examples and formatted results in the same window;
  explicit Format/Ctrl+Enter for custom SQL, project overlay, cancellation
  and stale-result suppression. Preview never changes an IDE document.
- Save named configuration snapshots and choose a persistent default from
  built-in/native/user profiles. Older profile arrays remain readable;
  OK commits the draft and Cancel discards it. Editing detaches the named
  default without modifying its saved snapshot.
- Bilingual manuals updated. Unit, menu-table and standalone WinForms smoke
  checks added; installed-host verification for these new package versions
  remains separate from the previous SC-27 smoke evidence.

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
