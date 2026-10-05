# Changelog

This repository has no published stable release. The entries below describe
locally built preview packages; version numbers belong to separate adapters.

## Editor commands and settings redesign — 2026-10-05

- Visual Studio VSIX 0.2.10 / SSMS VSIX 0.6.9.
- Resolve the active document frame on each invocation, retaining editor access
  after menu/tool-window focus changes and falling back to buffer persistence.
- Two menu entries: Format Document and Settings; the same formatting command
  is placed in the SQL editor context menu and supports a configurable shortcut.
- Format only selected tokens/SQL statements, or the active document with no
  selection. Outside text is retained, and each edit remains one Undo operation.
- Two-tab profile editor with synchronized profile selection, import/export,
  save without closing, grouped options, bounded numeric inputs, SQL highlighting,
  Russian/English localization and IDE theme colors. Removed auxiliary labels.
- Updated both user manuals and installation guides. New-package validation is
  recorded separately from historical installed-host smoke results.

## Four IDE generations and release directory — 2026-10-05

- Visual Studio VSIX 0.2.9: separate packages for VS 2022 17.14 x64 and VS 2026 18.x x64.
- SSMS VSIX 0.6.8: separate packages for SSMS 20 x86 and SSMS 22 x64. The SSMS 20
  adapter compiles against SDK 15 and includes a compatibility bridge for task
  error logging. Shared formatter, settings and commands remain the same.
- Added an SSMS 20 deployment script with host/package checks and update backups;
  the legacy shell cannot use the modern VSIX Installer.
- `Build-Release.ps1` produces four installers, three converted native profiles,
  CLI preview and SHA-256 manifest in root `release/`. The previous candidate
  script invokes the same process. AGENTS.md preserves the delivery rule.
- Release build, 1238 Core + 145 golden tests, four package checks, settings UI
  smoke, CLI package smoke and 180 extracted-package profile cases passed.
  Installed-host checks and their limits are recorded in `release/VALIDATION.md`.
- Updated Russian/English user manuals and distribution installation guides.

## SQL Prompt additional object audit — 2026-10-02

- Visual Studio VSIX 0.2.7 / SSMS VSIX 0.6.6: refined ADIR_SQL_Main profile for
  CREATE/ALTER FUNCTION parameters, column constraints and explicit function RETURNS /
  view AS/query layout. Saved user profiles require reimporting the updated JSON.
- Added optional function parameter wrapping and column constraint alignment with
  Russian/English editor labels; new options default to inherit/false.
- Corrected bracket matching for temporal PERIOD, inline INDEX and derived table
  alias columns; JOIN boolean operators indent from ON. Function parameters now
  participate in type/default alignment.
- Read-only 2026 Git audit: 222 files from Functions/Tables/Triggers/Types/Views,
  143 exact matches, 78 stable differences and one preserved multiline-token file.
  All parse/token/idempotence checks passed. Rechecked 441 procedures: 68 exact,
  369 stable differences and four preserved; no validation failures in all 663 files.
- Both manuals and audit documentation updated. Release build, 1195 Core + 145
  golden tests, settings UI smoke and VSIX package checks passed.

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
