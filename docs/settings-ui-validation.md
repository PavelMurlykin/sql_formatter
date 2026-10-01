# Formatting settings UI redesign — 2026-10-01

Packages: Visual Studio 0.2.5 / SSMS 0.6.4. CLI formatting and JSON rule IDs
are unchanged. This work is separate from the completed SC-27 parity audit.

## Automated evidence

Debug and Release builds pass with no warnings. Both test configurations pass
1154 Core tests and 145 golden tests (1299 total); the separate shared-control
smoke also passes. The release-candidate pipeline verified both VSIX packages,
CLI packaging/install/stdin smoke and generated checksums in the Git-ignored
`artifacts/settings-ui-candidate-final/` directory. Native profile artifacts and
all 82 parity golden cases remain reproducible without regeneration.

- Catalog coverage tests visit all 1339 scalar fields in Russian and English,
  every category path, choice and dependency description. Missing vocabulary
  entries fail rather than fall back to technical camelCase labels.
- Search tests cover Russian multi-word category/name queries, case folding,
  technical-key aliases, no matches and grouped compound-rule members.
- Every distinct bundled SQL example parses and produces a Core preview.
- Profile tests cover legacy array migration, built-in/native/user defaults,
  case-insensitive selection, persistence, name collisions, replacement,
  independent drafts and rejection of malformed/unknown defaults.
- Both VSCT tables are checked for a top-level SQL Formatter menu and settings
  / profiles commands; both actual VSIX projects compile those menu resources.
- `scripts/Verify-SettingsUi.ps1` builds and runs the real shared controls in a
  separate WinForms host on Windows. It exercises initial tree/preview,
  compound numeric edits, dynamic filtering, custom SQL formatting, retained
  custom SQL after navigation, stale-output clearing, saving a profile,
  selecting/persisting its default, detaching it on edit, invalid SQL and
  minimum window sizing. It does not automate external IDE windows or persist
  user settings. Use `-RestoreSource C:\path\to\nuget-cache` offline.
- For visual inspection, launch
  `tests/TSqlFormatter.SettingsUiSmoke/bin/Release/net472/TSqlFormatter.SettingsUiSmoke.exe`
  after building it. No arguments opens an isolated draft; `--verify` runs
  automated checks and exits. This host is not an IDE persistence test.

## Remaining installed-host checks

The prior installed-package smoke applies only to VS 0.2.4 / SSMS 0.6.3;
it does not certify the redesigned window in the new packages. Windows
Computer Use launch approval timed out during this run; no visual result is
claimed from that attempt. Automated control checks and builds passed.

After updating the appropriate VSIX and restarting each IDE, check:

1. SQL Formatter is a separate menu in the main menu bar. Settings and saved
   profiles open the same owned, resizable modal window on the intended tab.
2. Search `EXEC вертикально`; change a choice and a compound indentation rule,
   inspect labels and layout at normal/minimum size and the host's display DPI.
3. Enter SQL, insert a newline with Enter, format with Ctrl+Enter, switch rules
   and restore the bundled example; no active IDE document should change.
4. Save a disposable named profile, select it by default and accept with OK.
   Reopen settings/restart IDE, confirm persistence and Document formatting.
5. Edit settings and profile selection, then Cancel; reopening must restore
   the previous applied state. Import/export native JSON; test overwrite
   confirmation separately from draft cancellation.
6. Confirm project overrides and document Undo retain their existing behavior.

Do not connect to a server or execute SQL for these checks. If SSMS opens a
Connect/authentication dialog, the user must dismiss it manually.
