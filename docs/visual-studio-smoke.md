# Visual Studio 2026 installed-VSIX smoke matrix

## SC-27 installed-package smoke — 2026-10-01

Host: Community 2026 18.10.2 (18.10.12217.157), x64, instance `f4993804`.
Local package 0.2.4 installed successfully on 2026-10-01 (installer exit 0,
installed manifest verified). No database
connection or SQL execution was used; only `artifacts/parity-ui/vs-smoke.sql`.

All settings opened from the Settings link. Its Settings/Profiles/Preview
tabs and action buttons were accessible. Search in the preceding 0.2.3 run
found the typed `rules.execute.parameters.stackList` field and its
inherit/on/off choices; that search was not repeated in 0.2.4.
ReadableVertical imported through Profiles → Import JSON, enabled full
settings, persisted with OK, and was still enabled when reopening the page.
Document formatted EXEC parameters, SELECT and CREATE TABLE, preserved
`N'KeepCase'`, and wrote the disposable file to disk. One Undo restored the
original source, which was saved afterward. Use full settings was then
turned off with OK to restore the original command mode.

The native multiline preview displayed LF output on one visual line in 0.2.3.
In 0.2.4 the sample's SELECT/FROM/WHERE and conditions are visibly on separate
lines. CRLF normalization is display-only; configured formatter/editor line
endings are unchanged. ReadableVertical import, OK persistence, reopening with
Use full settings checked, Document on EXEC/SELECT/CREATE TABLE, saving and one
Undo restoring the original three-line file were repeated in 0.2.4. The
literal stayed unchanged. Full settings was disabled with OK after the test.
Named profile save/load/export, Cancel rollback and project preview have not
been manually verified; model/preview tests cover those underlying workflows.
The earlier 0.2.1 command checks below remain historical, not evidence for all
new controls. The first 0.2.4 installation returned 2004 because VS was still
closing; retry after process exit succeeded. No security dialog was automated
and no IDE update was authorized. Static candidate/package checks also pass.

## Earlier command smoke — 2026-09-29

Run date: 2026-09-29. Host: Visual Studio Community 2026, 18.10.2
(18.10.12217.157), x64, instance `f4993804`, Windows. Package: local
`TSqlFormatter.VisualStudio.vsix` 0.2.1, not published.

Use two disposable `.sql` files. The first contains
`select Id, Name from dbo.Items where Id=1;` and
`select Id from dbo.Other where Id=2;` on separate lines. The second contains
`select Code from dbo.Codes where Code like 'A%';`. Do not connect to a database.
In each row, restore the source with Undo before the next formatting test.

| Check | Observed result |
| --- | --- |
| Install/remove | Per-user installation of 0.2.0 was detected; quiet uninstall returned 0 and removed its extension folder. Quiet installation of 0.2.1 returned 0, its installed manifest showed 0.2.1, and Visual Studio started with the updated Tools menu. |
| Document | Both statements were formatted; one `Ctrl+Z` restored the exact original. A lower-case keyword option produced lower-case keywords. |
| Selection | Selecting the first statement formatted only that statement, kept its result selected, and one `Ctrl+Z` restored it. |
| Statement | With the caret in the second statement, only that statement changed; one `Ctrl+Z` restored it. The caret remained within that statement, but logical-token preservation is not guaranteed. |
| Save | `Ctrl+S` wrote the formatted first file to disk; the disk text was inspected. `Format on save` remained Off, so its automatic modes were **not tested**. |
| Multiple open files | With both SQL files open, formatting the second did not change the first. One Undo restored the unsaved second file. |
| Options | `Tools → Options → T-SQL Formatter → General` opened the classic options dialog from Visual Studio 2026's Settings page. Changing keyword casing to Lower affected Document formatting; Upper was restored afterward. Other options pages were **not tested**. |
| Notifications | A successful formatting message appeared in the status bar. Error messages and Output-pane activation were **not tested**. |
| Diagnostic commands | Editor/Replace Selection/Background Probe were present in 0.2.0. They were absent from the reinstalled 0.2.1 Tools menu; Document, Selection, and Statement remained visible. |

The tests are deliberately narrow: no claim is made about other Visual Studio
builds, very large files, complex SQL, configuration errors, automatic save,
profile import/export, paste, or cancellation. The VSIX package is also checked
by `scripts/Verify-Vsix.ps1`; that static check is not an IDE runtime test.
