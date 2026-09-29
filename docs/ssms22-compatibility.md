# SSMS 22 compatibility boundary

Run date: 2026-09-29. Host: SSMS 22.10.1 (22.10.12210.168), x64,
instance `411fbe8f`. Package: local `TSqlFormatter.Ssms.vsix` 0.6.0.
No database connection was used. This is a manual smoke test, not a general
certification of SSMS 22.

| Host | Install and restart | Formatting and Undo | Options | Result |
| --- | --- | --- | --- | --- |
| SSMS 22.10.1, x64 | Old per-user 0.5.0 extension was detected and removed with exit code 0; its folder disappeared. 0.6.0 installed with exit code 0; its installed manifest showed 0.6.0. All three commands appeared on the first launch and after closing/relaunching SSMS. | Document formatted two SELECT statements; Selection changed only the selected first statement; Statement changed only the caret-targeted second statement. Each was restored by one `Ctrl+Z`. Two SQL tabs remained independent. A separate CTE + `LIKE` + `IS NOT NULL` + `ROW_NUMBER` frame formatted successfully. | General page opened from the Settings link. Keyword casing Lower changed Document output and was restored to Upper. Other controls/profiles were not exercised. | Limited manual smoke passed. |
| SSMS 22.10.2 and other 22.x, x64 | Not tested | Not tested | Not tested | No compatibility claim. The `[22.0,23.0)` VSIX target is only an installation range. |
| SSMS 21 or older, 32-bit hosts | Outside the VSIX target | Not tested | Not tested | Unsupported by this package. |

The installed SSMS application contains ScriptDom `18.0.56.2`, while the VSIX
packages `18.0.107.0`. The smoke cases above ran in the host successfully, but
they do not prove every formatter path against the host's assembly/API surface.
The earlier [integration spike](ssms-spike.md) explains the compatibility shim
for the optional `OrderByClause.All` member.

**Decision: LIMITED SUPPORT.** Offer this adapter only as an experimental,
version-specific option. Do not advertise support for all SSMS 22 releases.
Remaining gaps include an additional SSMS build, server-connected windows,
more ScriptDom constructs, profile and configuration precedence in the live
host, error paths, and repeated regression runs. Core, CLI, and Visual Studio
are not gated by these SSMS gaps. Microsoft states that third-party extensions
are not officially supported in SSMS in its
[FAQ](https://learn.microsoft.com/en-us/ssms/faq); users should treat this VSIX
accordingly.
