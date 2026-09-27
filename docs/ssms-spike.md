# SSMS 22 integration spike

Target: SSMS 22.10.1 (build 22.10.12210.168), x64, installed as instance `411fbe8f`.

## P15-001 — package loading

- The installed SSMS core manifest uses the VSIX target `Microsoft.VisualStudio.Ssms`, version `[22.0,)`.
- `TSqlFormatter.Ssms.vsix` targets `Microsoft.VisualStudio.Ssms` `[22.0,23.0)` and was installed per-user with SSMS's `VSIXInstaller.exe`.
- On a fresh SSMS launch, ActivityLog recorded `Begin package load [SsmsPackage]`, `SSMS spike package initialized.`, and `End package load [SsmsPackage]` for package `{908068E6-40D9-4543-AB9F-1B952930F1E3}`.
- The package auto-loads in the `NoSolution` UI context. The Tools menu command is a separate manual probe; its click has not been automated.

This confirms extension loading for this particular installation, not stability across SSMS versions or functionality of later spike stages.
