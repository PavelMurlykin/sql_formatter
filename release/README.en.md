# Installing T-SQL Formatter

Local build dated 2026-10-31. Choose the package for your IDE:

| IDE | File | Extension version |
| --- | --- | --- |
| Visual Studio 2022 17.14.x, x64 | `TSqlFormatter.VS2022.vsix` | 0.2.13 |
| Visual Studio 2026 18.x, x64 | `TSqlFormatter.VS2026.vsix` | 0.2.13 |
| SSMS 20.x, x86 | `TSqlFormatter.SSMS20.vsix` + `Install-SSMS20.ps1` | 0.6.12 |
| SSMS 22.x, x64 | `TSqlFormatter.SSMS22.vsix` | 0.6.12 |

The ready-made extensions do not require the .NET 10 SDK. They target .NET Framework 4.7.2 and bundle the formatting dependencies. Update a VS 2022 version earlier than 17.14 to the 17.14 branch.

## Visual Studio 2022 and 2026

Close the corresponding Visual Studio, double-click its VSIX, select the intended IDE instance, and complete installation. Restart the IDE. Commands are under **Extensions → SQL Formatter**; open a `.sql` file and select **Format Document**. SQL is never executed. One Ctrl+Z undoes the edit; save the file separately.

## SSMS 20

Close SSMS 20. Keep the VSIX and script in the same folder. Open PowerShell **as Administrator**, change to that folder, and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SSMS20.ps1
```

For a custom SSMS location, pass the directory containing `Ssms.exe`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SSMS20.ps1 -IdeDirectory "D:\Apps\SSMS20\Common7\IDE"
```

The script checks the SSMS generation and package contents, installs into `Extensions\TSqlFormatter.Ssms20`, and backs up the previous installation under `%LOCALAPPDATA%\TSqlFormatter\SSMS20\Backups`. Managed backups left inside the IDE by the old installer are migrated there: SSMS could discover their `.pkgdef` files and load an old DLL. Unknown folders are not overwritten or moved. Use `-BackupDirectory` for another location outside the IDE directory. Replace the old script along with the VSIX. The standard VSIX Installer cannot install this legacy-shell package: the VSIX is the script's payload container.

Start SSMS 20, open a `.sql` file without connecting to a server, and select **SQL Formatter → Format Document**.

## SSMS 22

Close SSMS 22. Run the VSIX Installer supplied with your SSMS 22 installation, passing `TSqlFormatter.SSMS22.vsix`. Example for the standard location with `release` as the current folder:

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" (Join-Path $PWD 'TSqlFormatter.SSMS22.vsix')
```

Select the SSMS 22 instance in the installer, then restart it. Find the **SQL Formatter** menu; formatting and analysis are also available in the SQL editor context menu.

## New features and post-upgrade checks

The packages include completed stages 1–9: TSQL001–TSQL005 analysis, snippets,
rename, outline/navigation, SQL transformations, CRUD from CREATE TABLE and local
history. Analysis and history settings are under **Settings… → General**.

- Enable/disable analysis and individual rules, and choose their severities.
  Run **Analyze Document** explicitly. Supplied `analysis` fields in the nearest
  `.tsqlformatter.json` override per-user IDE defaults.
- History captures save and normal close, including untitled SQL and discarded
  edits. Defaults: enabled, 100 snapshots, 1000000 characters, no age limit.
  Limits: 1–10000 snapshots, 1–16777216 characters and 0–36500 days (0 means no age limit).
- Use **Save** / **Save and close**. Closing without saving discards the settings
  draft. Formatting profiles do not change these settings. History is independent
  of format-on-save; disabling collection keeps existing files under
  `%USERPROFILE%\.tsqlformatter\history`.

Use a separate `.sql` file in a directory without project configuration. Analyze
`SELECT * FROM dbo.ReleaseSmoke;` and expect TSQL001. Save a unique comment, add
more text, then close without saving. Find both states with **Restore Closed Script**;
also check closing a new untitled SQL document. Restore opens text in a new document.
No SQL execution is needed. Server/database fields are optional; live connected
context capture was not tested. Fresh installation of these exact versions in all
four IDEs was not repeated for this delivery; automated and previous installed-host
checks are distinguished in [validation](VALIDATION.md).

## Profiles

The distribution includes native JSON v2 profiles:

- `ADIR_SQL_Main.json` — settings prepared from the SQL Prompt profile;
- `AV_Profile.json` and `Right-aligned-EPM-AWB2.json` — settings from SQL Complete profiles, with 573 rules each.

Open **SQL Formatter → Settings… → General** (in Visual Studio open **Extensions** first), click **Import profile…**, select a file, check its name and click **Save**. Repeat for each profile. Select the active profile on either tab. **Format Document** formats the selection, or the whole active `.sql` without a selection; it also appears in the editor context menu. On **General**, press the desired shortcut in its field and save it. The window follows the IDE UI language and theme.

On **Formatting**, the tree contains four bold groups and twelve pages at one child level. Edit details on the right; search narrows pages and their parameter sections.

This build's **ADIR_SQL_Main** removes optional square brackets, terminates statements
with `;` and aligns `ELSE IF` chains. After updating the VSIX, import
`ADIR_SQL_Main.json` again, replace the saved copy with the same name, select it as
active and click **Save**. Find the new rules under
**Formatting → Global → Basics → SQL syntax**; required brackets are retained.

The nearest `.tsqlformatter.json` overrides the selected IDE profile. For project settings, copy one JSON under that name beside your SQL files, backing up existing configuration first. Third-party XML/style files are not imported directly. The converted settings contain documented approximations and do not promise identical SQL Prompt/SQL Complete output.

[Validation results](VALIDATION.md), [English manual](../docs/user-manual.en.md), [русская инструкция установки](README.ru.md). `release-manifest.json` records installer/profile SHA-256 values; `profile-verification.json` records automated profile checks. The additional CLI package requires the .NET 10 Runtime.
