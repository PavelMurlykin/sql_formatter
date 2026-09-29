# Changelog

This repository has no published stable release. The entries below describe
locally built preview packages; version numbers belong to separate adapters.

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
