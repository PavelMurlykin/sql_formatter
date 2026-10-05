# Installing T-SQL Formatter

Local build dated 2026-10-05. Choose the package for your IDE:

| IDE | File | Extension version |
| --- | --- | --- |
| Visual Studio 2022 17.14.x, x64 | `TSqlFormatter.VS2022.vsix` | 0.2.9 |
| Visual Studio 2026 18.x, x64 | `TSqlFormatter.VS2026.vsix` | 0.2.9 |
| SSMS 20.x, x86 | `TSqlFormatter.SSMS20.vsix` + `Install-SSMS20.ps1` | 0.6.8 |
| SSMS 22.x, x64 | `TSqlFormatter.SSMS22.vsix` | 0.6.8 |

The ready-made extensions do not require the .NET 10 SDK. They target .NET Framework 4.7.2 and bundle the formatting dependencies. Update a VS 2022 version earlier than 17.14 to the 17.14 branch.

## Visual Studio 2022 and 2026

Close the corresponding Visual Studio, double-click its VSIX, select the intended IDE instance, and complete installation. Restart the IDE. Commands are under **Extensions → SQL Formatter**; open a `.sql` file and select **Format T-SQL Document**. SQL is never executed. One Ctrl+Z undoes the edit; save the file separately.

## SSMS 20

Close SSMS 20. Keep the VSIX and script in the same folder. Open PowerShell **as Administrator**, change to that folder, and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SSMS20.ps1
```

For a custom SSMS location, pass the directory containing `Ssms.exe`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SSMS20.ps1 -IdeDirectory "D:\Apps\SSMS20\Common7\IDE"
```

The script checks the SSMS generation and package contents, installs into `Extensions\TSqlFormatter.Ssms20`, and backs up an earlier installation outside `Extensions` before updating. It refuses to overwrite an unknown existing folder. SSMS 20 uses a legacy shell, so the standard VSIX Installer cannot install this package: the VSIX is the script's payload container.

Start SSMS 20, open a `.sql` file without connecting to a server, and select **SQL Formatter → T-SQL Formatter (SSMS): Format Document**.

## SSMS 22

Close SSMS 22. Run the VSIX Installer supplied with your SSMS 22 installation, passing `TSqlFormatter.SSMS22.vsix`. Example for the standard location with `release` as the current folder:

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" (Join-Path $PWD 'TSqlFormatter.SSMS22.vsix')
```

Select the SSMS 22 instance in the installer, then restart it. Use the top-level **SQL Formatter** menu.

## Profiles

The distribution includes native JSON v2 profiles:

- `ADIR_SQL_Main.json` — settings prepared from the SQL Prompt profile;
- `AV_Profile.json` and `Right-aligned-EPM-AWB2.json` — settings from SQL Complete profiles, with 573 rules each.

Open **SQL Formatter → Сохранённые профили…** (Saved profiles; in Visual Studio open **Extensions** first), click **Импорт JSON…** (Import JSON), select a file, enter its name, and click **Сохранить профиль** (Save profile). Optionally select **Использовать по умолчанию** (Use by default), then **OK**. Import and save each profile separately.

The nearest `.tsqlformatter.json` overrides the selected IDE profile. For project settings, copy one JSON under that name beside your SQL files, backing up existing configuration first. Third-party XML/style files are not imported directly. The converted settings contain documented approximations and do not promise identical SQL Prompt/SQL Complete output.

[Validation results](VALIDATION.md), [English manual](../docs/user-manual.en.md), [русская инструкция установки](README.ru.md). `release-manifest.json` records installer/profile SHA-256 values; `profile-verification.json` records automated profile checks. The additional CLI package requires the .NET 10 Runtime.
