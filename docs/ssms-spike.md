# SSMS 22 integration spike

This is a historical 0.2–0.5 VSIX record. See the current
[0.6.0 compatibility matrix](ssms22-compatibility.md) for phase 29 results.

Target: SSMS 22.10.1 (build 22.10.12210.168), x64, installed as instance `411fbe8f`.

## P15-001 — package loading

- The installed SSMS core manifest uses the VSIX target `Microsoft.VisualStudio.Ssms`, version `[22.0,)`.
- `TSqlFormatter.Ssms.vsix` targets `Microsoft.VisualStudio.Ssms` `[22.0,23.0)` and was installed per-user with SSMS's `VSIXInstaller.exe`.
- On a fresh SSMS launch, ActivityLog recorded `Begin package load [SsmsPackage]`, `SSMS spike package initialized.`, and `End package load [SsmsPackage]` for package `{908068E6-40D9-4543-AB9F-1B952930F1E3}`.
- The package auto-loads in the `NoSolution` UI context. The Tools menu command is a separate manual probe; its click has not been automated.

This confirms extension loading for this particular installation, not stability across SSMS versions or functionality of later spike stages.

## P15-002 — active query editor

- The command table must be embedded as `Menus.ctmenu` to match `ProvideMenuResource`; without the explicit VSCT resource name, the package loaded but the Tools commands were absent. The same resource metadata was corrected in the Visual Studio project.
- After installing SSMS VSIX 0.2.4, both probe commands appeared in the SSMS 22.10.1 Tools menu.
- With `ssms-editor-probe.sql` open and no database connection, Query Editor Probe reported its file name and 10 characters. After an unsaved edit, it reported 19 characters. The command reads the live `IVsTextLines` buffer and never logs SQL content.
- Editing, formatting, options and compatibility across other SSMS versions remain unverified.

## P15-003 — format document

- SSMS VSIX 0.3.1 registers Format Document and packages the shared Core and Configuration projects. It formats an active `.sql` buffer with configuration discovery, rejects parse/configuration errors and stale snapshots, and applies the result in one Undo transaction.
- SSMS 22.10.1 loads its own ScriptDom 18.0.56.2 before the VSIX's 18.0.107.0. The older copy lacks `OrderByClause.All`; Core now queries this optional member late for compatibility with both copies.
- In the installed SSMS, `select 1;` became `SELECT 1;` without a database connection. One Ctrl+Z restored the original lowercase text. The test edit was not saved.
- This is a smoke test of one SQL example, not proof of compatibility across all ScriptDom APIs or SSMS versions.

## P15-004 — selection, caret and Undo

- SSMS VSIX 0.4.0 adds Format Selection and Format Statement. Each uses a single scoped `FormatResult` edit, maps the selection/caret through that edit, and completes one editor Undo transaction only after applying the replacement.
- In SSMS 22.10.1, selecting the first of two `select` statements formatted only the first, kept its text selected, and one Ctrl+Z restored it. With the caret in the second statement, Format Statement changed only the second, left the caret on its line, and one Ctrl+Z restored it.
- The SQL test document remains unsaved; other SSMS versions and more complex editor states have not been checked.

## P15-005 — options

- SSMS VSIX 0.5.0 registers a per-user `T-SQL Formatter (SSMS) → General` options page with Default/Compact/Expanded profiles and Default-profile controls for line width/ending, final newline, indentation, tabs, and keyword casing. A nearby JSON config still takes precedence.
- In SSMS 22.10.1, the page appeared under Tools → Options. Setting Keyword casing to Lower made Format Document turn a temporary uppercase `SELECT` into lowercase `select`. The original Upper setting was restored, and the temporary SQL edit was discarded without saving.
- This verifies one option-to-command path, not every option or profile on this host.

## P15-006 — compatibility matrix and decision

The VSIX manifest targets **64-bit SSMS 22.x**, but a manifest range is not evidence that every 22.x release works. The matrix records observations for the installed package, not a support promise. “Not tested” must not be read as “works.”

| SSMS version | Tested | Command | Selection | Options | Known issues / gaps |
| --- | --- | --- | --- | --- | --- |
| 22.10.1 (22.10.12210.168), x64 | Yes, manual smoke tests | Format Document, Format Selection and Format Statement executed on disposable SQL; each changed only the intended text and one Undo reverted it | One selected statement and one caret-targeted statement tested | General page displayed; `Keyword casing = Lower` affected Format Document and was restored to Upper | Host preloads ScriptDom 18.0.56.2; the packaged newer ScriptDom API cannot be assumed available. Only simple SELECT input was exercised. Multiple query windows, restart persistence, every setting/profile, and uninstall/reinstall were not tested. |
| Other SSMS 22.x, x64 | No | Not tested | Not tested | Not tested | Manifest permits installation but runtime compatibility is unknown. |
| SSMS 21 and earlier, or 32-bit hosts | No | Not supported by VSIX target | Not tested | Not tested | Outside the declared installation target. |

**Decision: LIMITED SUPPORT.** The spike proves a working path for the three formatting commands and a basic option in one SSMS 22.10.1 installation. It does not justify a broad SSMS compatibility claim. Phase 16 may proceed as an explicitly experimental MVP for this tested host; a supported release remains gated on multi-window/restart/install-uninstall testing, complex SQL coverage against the host's ScriptDom, and a repeatable version-by-version regression run. Core, CLI and Visual Studio development do not depend on this adapter.
