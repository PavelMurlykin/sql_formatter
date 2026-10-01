# SSMS 22 compatibility boundary

## SC-27 installed-package smoke — 2026-10-01

Host: SSMS 22.10.2 (22.10.12217.157), x64, instance `411fbe8f`.
Package: local 0.6.3, quiet installer exit 0 and installed manifest verified.
Host ScriptDom: 18.0.105.0; bundled assembly: 18.0.107.0.
Only the disposable `artifacts/parity-ui/ssms-smoke.sql` was used. The user
cancelled Connect on startup and file opening; no database connection or
SQL execution was used.

| Check | Observed result |
| --- | --- |
| All settings | Settings/Profiles/Preview tabs opened. Search found `rules.execute.parameters.stackList`; selecting on and Set value marked it explicit and enabled full settings. Cancel then reopening restored inherit and full settings off. |
| Native import / persistence | ReadableVertical JSON v2 imported, enabled full settings and persisted with OK; reopening retained the enabled state. |
| Preview | The default sample showed separate SELECT/list/FROM lines. Project preview displayed the repository `.editorconfig` as its source. No document edit occurred. |
| Document / host API | EXEC, SELECT and defined CREATE TABLE formatted successfully, preserving `N'KeepCase'`, with the success message. No missing-member error occurred in this CREATE TABLE path on host ScriptDom 18.0.105.0. |
| Save / Undo | Formatted text was saved and inspected on disk. One Ctrl+Z restored the original three-line script; the original was saved. Full settings was disabled with OK afterward. |

**Decision: LIMITED SUPPORT** for this exact host/package smoke, not all
SSMS 22.x. Named profile save/load/export, other project precedence cases,
connected windows, errors/cancellation, large files and all ScriptDom forms
remain untested in the installed adapter. Core/CLI tests do not remove these
runtime boundaries. The following older results are historical.

## Earlier command smoke — 2026-09-29

Run date: 2026-09-29. Host: SSMS 22.10.1 (22.10.12210.168), x64,
instance `411fbe8f`. Package: local `TSqlFormatter.Ssms.vsix` 0.6.0.
No database connection was used. This is a manual smoke test, not a general
certification of SSMS 22.

| Host | Install and restart | Formatting and Undo | Options | Result |
| --- | --- | --- | --- | --- |
| SSMS 22.10.1, x64 | Old per-user 0.5.0 extension was detected and removed with exit code 0; its folder disappeared. 0.6.0 installed with exit code 0; its installed manifest showed 0.6.0. All three commands appeared on the first launch and after closing/relaunching SSMS. | Document formatted two SELECT statements; Selection changed only the selected first statement; Statement changed only the caret-targeted second statement. Each was restored by one `Ctrl+Z`. Two SQL tabs remained independent. A separate CTE + `LIKE` + `IS NOT NULL` + `ROW_NUMBER` frame formatted successfully. | General page opened from the Settings link. Keyword casing Lower changed Document output and was restored to Upper. Other controls/profiles were not exercised. | Limited manual smoke passed. |
| Other SSMS 22.x builds, x64 | Not tested | Not tested | Not tested | No compatibility claim. The `[22.0,23.0)` VSIX target is only an installation range. |
| SSMS 21 or older, 32-bit hosts | Outside the VSIX target | Not tested | Not tested | Unsupported by this package. |

The installed SSMS application contains ScriptDom `18.0.56.2`, while the VSIX
packages `18.0.107.0`. The smoke cases above ran in the host successfully, but
they do not prove every formatter path against the host's assembly/API surface.
The earlier [integration spike](ssms-spike.md) explains the compatibility shim
for the optional `OrderByClause.All` member.

**Decision: LIMITED SUPPORT.** Offer this adapter only as an experimental,
version-specific option. Do not advertise support for all SSMS 22 releases.
The newer 22.10.2 smoke above does not certify the 0.6.3 package on 22.10.1.
Remaining gaps include further SSMS builds, server-connected windows,
more ScriptDom constructs, profile and configuration precedence in the live
host, error paths, and repeated regression runs. Core, CLI, and Visual Studio
are not gated by these SSMS gaps. Microsoft states that third-party extensions
are not officially supported in SSMS in its
[FAQ](https://learn.microsoft.com/en-us/ssms/faq); users should treat this VSIX
accordingly.
