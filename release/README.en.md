# Installing T-SQL Formatter

Local build dated 2026-11-13. Choose the package for your IDE:

| IDE | File | Extension version |
| --- | --- | --- |
| Visual Studio 2022 17.14.x, x64 | `TSqlFormatter.VS2022.vsix` | 0.2.15 |
| Visual Studio 2026 18.x, x64 | `TSqlFormatter.VS2026.vsix` | 0.2.15 |
| SSMS 20.x, x86 | `TSqlFormatter.SSMS20.vsix` + `Install-SSMS20.ps1` | 0.6.14 |
| SSMS 22.x, x64 | `TSqlFormatter.SSMS22.vsix` | 0.6.14 |

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

The packages include completed stages 1–11: TSQL001–TSQL005 analysis, snippets,
rename, outline/navigation, SQL transformations, CRUD from CREATE TABLE and local
history, DEV/TEST/PROD environments and dangerous-command checks.
Stage 11 adds clipboard TSV to IN, INSERT and numeric-cell statistics.
Analysis, history and environment settings are under **Settings… → General**.

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
- In the environments grid, add DEV/TEST/PROD, substrings of available server/
  database names or the explicit document path/moniker, and a `#RRGGBB` color.
  Filled fields use AND and case-insensitive matching; at least one is required.
  Overlaps resolve as PROD > TEST > DEV; the first row wins within the same kind.
  Without a match the label is UNKNOWN; paths are never substituted for servers.
- **Check Dangerous Commands** in the menu and editor context menu checks the
  whole document for DROP (TSQL006), TRUNCATE TABLE (TSQL007), UPDATE/DELETE without
  WHERE (TSQL004/TSQL005), independently of disabled normal analysis. The dialog
  shows a colored label, a separate PROD warning and navigation to a selected row.
  Parse errors mean an incomplete check. Tabs are not recolored and execution is
  not intercepted; dynamic SQL and WHERE truth are not evaluated. USE, three-part
  names and procedure calls do not change the captured context label.

Use a separate `.sql` file in a directory without project configuration. Analyze
`SELECT * FROM dbo.ReleaseSmoke;` and expect TSQL001. Save a unique comment, add
more text, then close without saving. Find both states with **Restore Closed Script**;
also check closing a new untitled SQL document. Restore opens text in a new document.
No SQL execution is needed. Server/database fields are optional; live connected
context capture was not tested. Fresh installation of these exact versions in all
four IDEs was not repeated for this delivery; automated and previous installed-host
checks are distinguished in [validation](VALIDATION.md).

To check stage 10, create a separate `stage10-prod.sql` with the following text;
**do not execute it**:

```sql
DROP TABLE dbo.Stage10Smoke;
TRUNCATE TABLE dbo.Stage10Smoke;
UPDATE dbo.Stage10Smoke SET A = 1;
DELETE FROM dbo.Stage10Smoke;
```

Add a temporary PROD rule with **Path / moniker contains** `stage10-prod.sql`
and color `#B71C1C`, then save settings. Invoke the check: expect PROD, its separate
warning and four findings on lines 1–4. Select DROP and **Go to**: the cursor should
move to line 1 and the source text should stay unchanged. Remove the temporary
rule and save afterwards. Normal CLI `--analyze` retains TSQL001–TSQL005; the new
command is available in the IDE.

### Stage 11: IN, INSERT and clipboard statistics

Copy cells as TSV (tabs between columns, newlines between records). Return to the
SQL editor. Commands are under **SQL Formatter** and in the editor context menu;
in Visual Studio open **Extensions** first. All rows are data by default;
headers must be enabled explicitly in each command's dialog.

- **Paste as IN (…)**: copy one column with rows `1`, `2`, `NULL`. In separate
  test SQL, place the caret after `SELECT 1 WHERE 1` and a space, then invoke the command.
  Expect `IN (1, 2, NULL)` in the preview. **Apply**, then one Undo should restore
  the original text. **Cancel** makes no SQL edit.
- **Generate INSERT**: copy the TSV below, enable **First row contains headers**
  and enter `dbo.Stage11_TestOnly`. Column names and `O'Brien` must be preserved
  with escaping, and NULL must remain SQL NULL. Without headers, INSERT omits
  the column list. Output replaces the selection or inserts at the caret, with one Undo.

```text
Id	Name
1	O'Brien
2	NULL
```

- **Selection Statistics (Clipboard)**: copy the TSV below, leave headers off
  and invoke the command. Expect 4 cells, 2 numeric, sum 3.5 and average 1.75.
  The source is the clipboard, not selected SQL; an open SQL document is not required.

```text
1	2.5
NULL	text
```

**Do not execute** this synthetic SQL; no table is needed for these checks.
Commands open no connection and leave the clipboard unchanged. Output uses
the active profile with project settings. The preview shows its first line and
accessible action buttons at minimum window size too. Changed text, document
or caret/selection rejects insertion.

Auto mode infers each column; mixed data and leading zeros stay text. Empty
cells become `N''`, unquoted NULL becomes SQL NULL, and `"NULL"` stays a string;
ISO dates use CONVERT. **All values as text, including NULL** disables inference.
Table names have 1–3 parts and support [brackets]. INSERT splits at 1000 rows.
Review target types, IDENTITY and constraints before using the SQL: schema
metadata is not read.

TSV limits: 1 Mi characters, 10000 rows including headers, 256 columns, 100000 cells.
Quoted tabs/newlines are supported; unequal rows, NUL and invalid quotes are
rejected. Statistics skips text, sums decimal values exactly and rounds the
average; unrepresentable values/sums are rejected. No COM ResultsGrid, HTML/CSV
or new CLI commands are used. Full typing rules and limits are in the
[manual](../docs/user-manual.en.md#vs-and-ssms-query-results-through-the-clipboard).
These exact rebuilt packages were not freshly installed in this pass; the
earlier native VS2022 scenario and current package probes are separated in
[release validation](VALIDATION.md).

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
