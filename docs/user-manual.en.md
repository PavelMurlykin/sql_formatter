# T-SQL Formatter — user manual

## Current status

This is an early prototype. T-SQL parsing, token navigation, comment classification, offset-to-line mapping, layout document construction and rendering, keyword casing, and formatting for supported `SELECT`, `INSERT`, `UPDATE`, `DELETE`, and `MERGE` forms are available through `TSqlFormatter.Core`, including CTEs, subqueries, `CASE`, window functions, `FROM`, `JOIN`, `APPLY`, and simple control-flow and stored-code forms. JSON settings, named profiles, and a limited `.editorconfig` subset are available through `TSqlFormatter.Configuration`. The CLI formats SQL from stdin or one file to stdout; multiple files and directories support `--write` and `--check` with configuration discovery for each file. The CLI can be built and installed as a local preview `dotnet tool`; no stable package has been published. An experimental VSIX formats the entire open `.sql` document, a statement containing a selection, or the statement nearest the caret and loads file configuration. A separate SSMS 22 VSIX can format the active `.sql` document experimentally.

The repository now includes a verifiable requirements inventory for two user-supplied SQL Complete profiles and T-SQL examples for 20 categories. All 969 applicable ledger paths have native settings; 8 OptionHints paths were declared not applicable. This is capability coverage with documented safe-layout limitations, not byte-for-byte SQL Complete output. XML profiles are not imported. The available user settings are listed below under “JSON configuration.”

### Control flow and stored code

The CLI and editor commands use the same engine. For simple statements it lays out multiple-variable `DECLARE`, `SET`, `IF`/`ELSE`, `BEGIN`/`END`, `WHILE`, `BEGIN TRY`/`BEGIN CATCH`, `THROW`, and the bodies of `CREATE`/`ALTER PROCEDURE`, `FUNCTION`, and `VIEW` queries. For example, `begin set @a=1; set @b=2; end` becomes a `BEGIN` line, two indented `SET` lines, and a closing `END`. Formatting does not change string literals or execute SQL. If control-flow headers or statement gaps contain comments, or a shape is unsupported, the original layout is retained; recognized keyword casing may still change according to settings. Review the result before saving an important script.

### Column alignment

JSON configuration can enable `alignment.selectAliases`, `alignment.setAssignments`, and `alignment.declareTypes` (all `false` by default). The first aligns explicit `AS` keywords and aliases in a simple `SELECT` list; the second aligns `=` signs in simple `UPDATE ... SET` assignments; the third aligns data types in a multi-variable `DECLARE`. For example, with `"selectAliases": true`, `Id AS CustomerId, LongName AS Name` is placed on separate lines with the `AS` keywords in one column. Alignment falls back to the previous layout for a particular list when it contains comments or complex forms, uses tabs, or would exceed `general.maxLineLength`. It does not align a standalone `SET @x = ...` or implicit aliases. These settings are available in VS/SSMS All settings and `.tsqlformatter.json`; native profile import retains alignment.

## Setup

You need the project source and a .NET 10 SDK (newer 10.0 feature bands are accepted). Building the whole solution, including the VSIX scaffold, also requires Windows, Visual Studio 2026 with the Visual Studio extension development component, and NuGet access. To use only the CLI, you can build `src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj` without Visual Studio. The shared Core and Configuration remain compatible with `netstandard2.0`, and the IDE extensions with `net472`. Run these commands from the repository root:

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
dotnet test TSqlFormatter.sln --no-build --no-restore
```

To use the parser in your C# project, add a reference to `src/TSqlFormatter.Core/TSqlFormatter.Core.csproj`.

## Local dotnet tool and CI examples

The CLI packs as `TSqlFormatter.Tool` version `0.1.0-preview.6` with the `tsqlformat` command. Building needs a .NET 10 SDK and running needs a .NET 10 Runtime; a .NET 8 Runtime alone is insufficient. From the repository root, build and install into a Git-ignored directory (PowerShell):

```powershell
dotnet pack src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj -c Release -o artifacts/tool
dotnet tool install TSqlFormatter.Tool --tool-path artifacts/tool-bin --source artifacts/tool --version 0.1.0-preview.6
.\artifacts\tool-bin\tsqlformat.exe query.sql --check
```

The tool is not published on NuGet; these commands install only the locally built package, so the CI examples assume this project's source tree is available. When upgrading from preview `0.1.0-preview.1`, install the .NET 10 Runtime and reinstall the package. `tsqlformat` accepts stdin or one SQL file for stdout, and multiple files or a directory with `--check` or `--write`. For each file, `--check` returns `0` (formatted), `1` (formatting needed), or `2` (error); batch exit-code rules are described below.

`examples/ci/` contains optional [GitHub Actions](../examples/ci/github-actions.yml), [Azure Pipelines](../examples/ci/azure-pipelines.yml), and [pre-commit](../examples/ci/.pre-commit-config.yaml) examples plus the helper `check_sql_files.py`. Checking user SQL through these examples is not active in this repository. Replace `database` with your SQL directory and copy the YAML into the appropriate project location. The GitHub Actions and Azure Pipelines examples build the local package and run `tsqlformat database --check` directly, without Python. Pre-commit still uses Python 3 and the helper for passed `.sql` files; install `tsqlformat` beforehand and add its directory to `PATH`. The helper returns `0` if all files are formatted, `1` if changes are needed, and `2` on errors or a missing tool.

The repository also has an active [GitHub Actions workflow](../.github/workflows/ci.yml) for pushes/PRs to `main` and manual runs. It builds and tests Core/CLI on Linux and Windows, packs, installs, and smoke-tests the CLI, and builds the solution and verifies the Visual Studio VSIX package on Windows. The workflow does not publish a package, run the extension inside an IDE, or check user SQL files. Making checks mandatory for merging requires separate GitHub branch-protection settings.

## Local release candidate

To build the three current packages and verify their versions, tests, VSIX contents, and an installed CLI smoke case in one run, use `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-ReleaseCandidate.ps1` from the repository root on the Windows build host described above. If NuGet is blocked but pinned packages are already cached, add `-RestoreSource` with that local cache path. The packages and SHA-256 manifest are written to the Git-ignored `artifacts/release-candidate/` directory; use a new `-OutputDirectory` for another run. The script does not publish anything. The CLI remains `0.1.0-preview.6`, the Visual Studio VSIX is `0.2.1`, and the experimental SSMS VSIX is `0.6.0`. See the [release-candidate guide](release-candidate.md) and [changelog](../CHANGELOG.md) for installation, verified hosts, and open owner decisions about name, channels, and signing. No stable release is available yet.

## SSMS 22: experimental extension

Build `src/TSqlFormatter.Ssms/TSqlFormatter.Ssms.csproj` in Release. The resulting `src/TSqlFormatter.Ssms/bin/Release/net472/TSqlFormatter.Ssms.vsix` targets 64-bit SSMS 22; install it into SSMS, not Visual Studio. After restarting SSMS, the Tools menu contains commands prefixed `T-SQL Formatter (SSMS):`: Format Document, Format Selection, and Format Statement. Open a `.sql` file in the query editor. Format Document applies the shared formatter to the entire buffer. Format Selection formats the statement containing selected SQL; Format Statement formats the statement nearest the caret. Unrelated statements remain unchanged. These commands reject invalid SQL, buffers above 16 Mi characters, and edits made during formatting. If a target cannot be formatted safely, a diagnostic code is shown instead of an already-formatted message. Each replacement is one Undo operation and preserves the selection or caret position; save the file separately. No database connection is required.

Set SSMS defaults under `Tools → Options → T-SQL Formatter (SSMS) → General` (the General link opens a classic options dialog). The Default profile exposes maximum line length, line ending, final newline, indent size/tabs, and keyword casing. Compact and Expanded use their built-in settings instead of those Default fields. A `.editorconfig`, when present, overlays common whitespace settings; the nearest `.tsqlformatter.json` takes precedence over those and the SSMS profile. VSIX 0.6.0 was manually tested only in SSMS 22.10.1 (x64): install/remove, restart, all three commands, Undo, two tabs, keyword casing, and one CTE/window-frame sample. Other 22.x versions, server connections, and arbitrary complex SQL remain untested. The exact [compatibility matrix](ssms22-compatibility.md) records the LIMITED SUPPORT decision. Microsoft does not officially support third-party SSMS extensions, so this VSIX remains experimental.

## Visual Studio: experimental command

On Windows with Visual Studio 2026 and the Visual Studio extension development component, build `src/TSqlFormatter.VisualStudio/TSqlFormatter.VisualStudio.csproj` in Release configuration. The resulting `src/TSqlFormatter.VisualStudio/bin/Release/net472/TSqlFormatter.VisualStudio.vsix` is for integration testing only. Install the VSIX, restart Visual Studio, open a `.sql` file, and use the three `Tools → Format T-SQL ...` commands described below. Diagnostic probe commands are no longer included in the menu. The [installed-IDE smoke matrix](visual-studio-smoke.md) records the tested host and remaining gaps.

Before installation, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Vsix.ps1` from the repository root to check the preview VSIX manifest and required assemblies. This package check does not verify runtime behavior in Visual Studio.

`Tools → Format T-SQL Document` formats the entire open `.sql` document using the VSIX's General options as a baseline, overlaid by `.editorconfig` and then the nearest `.tsqlformatter.json` if found. Parsing and rendering run in the background. Before applying the result, the VSIX checks that the active buffer has not changed or been replaced, then applies it in one Undo transaction. Invalid SQL or configuration leaves the document unchanged and displays a diagnostic. Already-formatted SQL produces an informational message. The buffer limit is 16 Mi UTF-16 characters. Caret and selection positions are preserved approximately by offset; after large layout changes they may no longer point to the same logical SQL item. The command is visible for other file types but operates only on `.sql`. Document, Selection, Statement, Undo, save, and two open SQL files were manually checked with simple SQL in Visual Studio 2026; see the smoke matrix for untested cases.

`Tools → Format T-SQL Selection` requires a nonempty selection within one T-SQL statement. It formats the whole containing statement, but leaves other statements and surrounding text untouched. If a selection boundary cuts through a token (including a string or identifier), spans multiple statements, or the whole script cannot be parsed, the command leaves the document unchanged and displays a diagnostic. The result is applied as one edit and one Undo only if the buffer is still unchanged. It currently chooses a top-level statement rather than the smallest nested query; post-format selection position is approximate. The size limit and configuration discovery match Format Document.

`Tools → Format T-SQL Statement` formats only the top-level statement nearest the caret, even when the caret is in whitespace between statements. Other text stays unchanged. An empty script, caret outside the document, parse error, or a statement that cannot be safely formatted in isolation produces a diagnostic without an edit. Formatting runs in the background; one Undo edit is applied only to the unchanged active buffer. The 16 Mi-character limit and configuration discovery match the other commands. It does not yet choose the smallest nested statement.

The VSIX adds `Tools → Options → T-SQL Formatter → General` with maximum line length (1–4096, default 100), LF/CRLF/CR line ending (default LF), final newline (off), indent size (0–32, default 4), tabs (off), and keyword casing (Upper by default). In Visual Studio 2026, select the General link on the Settings page to open the classic options dialog. With the `Default` IDE profile, these values are the baseline for all three formatting commands. They are not CLI settings and do not write a project file. Common `.editorconfig` settings overlay the IDE baseline; the commands then search for `.tsqlformatter.json` from the open SQL file's directory upward to a `.git` marker or the filesystem root. Specified fields in the nearest JSON file take highest precedence. Without files, the IDE baseline applies. JSON is read as UTF-8 with limits of 64 MiB and 16 Mi decoded characters. File (`TSF9000`/`TSF9001`) or settings (`TSF2000`) errors are displayed without editing the buffer. Changing Keyword casing to Lower was manually confirmed to affect Document formatting; it was then restored to Upper.

`Tools → Options → T-SQL Formatter → SQL Preview` provides an editable SQL sample (up to 4096 characters) and a formatted result. After a 300 ms pause in typing it runs the same Core formatter as the commands; returning to the page refreshes the preview with the current IDE settings. The preview uses IDE options only, not a project config, and never edits the open SQL document. Invalid sample SQL displays a diagnostic instead of formatted output. Runtime behavior in an installed Visual Studio instance remains to be checked manually.

The `SELECT`, `JOIN`, and `WHERE` pages under `Tools → Options → T-SQL Formatter` configure supported layouts. SELECT offers `Auto` or `OnePerLine` for columns and for GROUP BY/ORDER BY items. JOIN can place supported JOIN/APPLY clauses and ON conditions on a new line (both on by default). WHERE can place supported WHERE/HAVING conditions and AND/OR operators on a new line (both on by default). With Use full settings disabled and the `Default` IDE profile, these page values are used by formatting commands and SQL Preview; matching fields in a project config override them for commands. Unsupported SQL constructs retain their original layout. Page behavior in a running IDE still needs manual verification.

`Tools → Options → T-SQL Formatter → Profile` selects `Default`, `Compact`, or `Expanded`. With Use full settings disabled, `Default` uses the General/SELECT/JOIN/WHERE page values; `Compact` uses its built-in 120-character width, while `Expanded` uses width 80 and one item per line for SELECT, GROUP BY, and ORDER BY. With `Compact` or `Expanded`, the other IDE pages are ignored until you switch back to `Default`; matching project-config fields still override the selected profile for formatting commands. SQL Preview uses the selected profile but not project configuration. Profile selection in the installed IDE needs manual verification.

`Tools → T-SQL Formatter: Export Profile...` saves effective IDE options as native v1/v2 JSON (v2 when rules are overridden). `Import Profile...` accepts native v1/v2 JSON up to 1 MiB, strict UTF-8; persists all rules and alignment in All settings, enables Use full settings, also updates legacy pages and selects Default. Neither the SQL document nor project configuration changes. Invalid files or values above this command's legacy limits (line width 4096, indent size 32) are rejected. For the full range use Import JSON in All settings. Both imports retain native types without interpreting XML; exported snapshots are portable as `.tsqlformatter.json`.

`Tools → Options → T-SQL Formatter → General → Format on save` defaults to `Off`. `CurrentDocument` formats the active `.sql` document immediately before Visual Studio writes it; `OnlyWhenProjectConfigExists` does so only when a `.tsqlformatter.json` is found by the normal upward search. Formatting uses the same project configuration, selected IDE profile, strict parsing, snapshot guard, and one Undo edit as the manual Document command. Automatic save formatting has a stricter 8 Ki-character buffer cap and a two-second cooperative cancellation token; manual Document retains its 16 Mi-character cap. A syntax/configuration error or cancellation does not block saving: the unformatted buffer is saved and an error is reported. The prototype processes only the active editor document; inactive files in Save All are not formatted. This save hook has compile and unit-test coverage, but still requires manual validation in a running Visual Studio instance before enabling it for valuable files.

`General → Save exclusions` accepts semicolon-separated, case-insensitive globs, for example `*.generated.sql; generated/**`. A pattern without a slash matches the file name; a pattern with a slash matches a path from any directory. `*` matches within a path segment, `**` crosses directories, and `?` matches one character. Exclusions apply to both save modes but never to manual formatting commands. An empty value excludes nothing.

`Tools → T-SQL Formatter: Paste Formatted SQL (Prototype)` reads up to 8 Ki characters of SQL text from the clipboard, formats that fragment with the selected IDE profile and project configuration, then replaces the current selection or inserts at the caret in an active `.sql` document. It uses a two-second cooperative cancellation token. The edit is one Undo transaction; on insertion the caret moves after the text. Invalid or empty clipboard SQL, a changed editor buffer, cancellation, or a parse/configuration error leaves the document unchanged. Only the pasted fragment is validated, not the combined document. This is an explicit opt-in command: ordinary `Ctrl+V` is unaffected, and automatic Format on Paste is not implemented. Test this prototype in a disposable file; runtime behavior still needs manual Visual Studio validation.

The automation limits follow a [synthetic local measurement](performance/editor-workflow-validation.md). Cancellation is cooperative; ScriptDom's synchronous parse can exceed the nominal two-second timeout before the token is observed. No UI responsiveness claim has been verified in an installed IDE.

Formatting status and errors appear in the Visual Studio status bar and the **T-SQL Formatter** Output pane; errors activate that pane. The messages include diagnostic codes when available. A successful status-bar message was confirmed in the installed IDE; error-pane behavior remains untested there.

## Full VS/SSMS settings editor (SC-25)

Open `Tools → Options → T-SQL Formatter → All settings` in Visual Studio or `Tools → Options → T-SQL Formatter (SSMS) → All settings` in the supported experimental SSMS. Both adapters share this page; its complete catalog comes from Core metadata, including all five indent members and both threshold members. Rule field names start with `rules.`, for example `rules.execute.parameters.stackList`; JSON uses the same key without that prefix inside `rules`.

Choose a category, search by part of a key/scope, and select a field. Set its boolean, numeric or choice value and click Set value. This enables Use full settings. For vertical EXEC parameters set `rules.execute.parameters.stackList = on`; for indentation set `rules.execute.parameters.listIndent.enabled = true` and `.offset = 1`. Reset field restores only the selected member; Reset all restores the draft without deleting named profiles. The explanation shows scope, explicit/inherited value, dependencies and dialect constraints. Disabled/dependent values are retained; for example stackMode applies when stackList=on, and local subquery rules require useSelectFormatting=false.

Use full settings is initially off: legacy pages and built-in profiles keep their existing behavior. When enabled, commands use the complete snapshot instead of legacy pages; Format on save/Save exclusions automation stays on General. OK/Apply persists the draft and named profiles in the IDE's per-user settings; Cancel discards unsaved changes. The SQL document is not modified.

Enter a name (1–100 characters) and Save as… to save a snapshot; replacing an existing name requires confirmation. Select a Built-in/User profile and click Load profile. Import JSON…/Export JSON… exchange native v1/v2 snapshots including rules/alignment; import is capped at 1 MiB and export confirms overwriting. File export is a separate action and is not undone by cancelling the options dialog. User names are stored in the IDE; they do not become CLI IDs for `--profile`.

Preview formats an editable sample up to 8192 characters in the background after a 300 ms pause; cancellation is cooperative with a 2-second limit. By default it shows only the draft, even if Use full settings is off. Include active SQL file's project configuration shows the `.editorconfig` and nearest `.tsqlformatter.json` overlay for the active saved `.sql`; the source line lists them in precedence order. A project override can prevent a local draft edit from changing the effective value. Project preview is unavailable without an active file. Configuration/SQL errors are shown without editing a document, and stale results are suppressed.

For CLI use, save the export as `.tsqlformatter.json` next to SQL and run `dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql`. Every rule is available through this shared format; there is no separate flag per field. With identical effective settings, preview/Core/CLI/IDE commands use the same engine. Build and automated tests verify the new page's implementation; installed-IDE manual verification remains part of SC-27 and is not established by earlier smoke-test records.

## Native alternatives to SQL Complete profiles (SC-26)

`examples/profiles/` provides two original ready-to-use v2 JSON styles: `ReadableVertical.json` (width 100, vertical lists, supported alignment and module boundaries) and `CompactQueries.json` (width 120, compact lists and margin-fitting queries). The latter does not flatten all procedural code. Import a file through All settings → Import JSON… and save a named profile; for CLI use it as `.tsqlformatter.json` next to SQL after backing up any existing configuration. These are settings files, not new `--profile` IDs.

They are **not AV/EPM conversions** or promises of identical output. Numeric XML modes/Style lack verified control output: the plan's native-alternative fallback is used instead of guessing. `sql-complete-correspondence.tsv` includes all 977 paths/577 groups, exact original values of both profiles, native counterparts, both alternatives' effective values, decision, test and semantics. All 969 applicable fields remain available for manual selection in the editor; only the 8 previously agreed OptionHints paths are excluded. An indent object in the report does not decode numeric XML Style. Original XML is not published.

To verify reproducibility from the repository root, run `pwsh -NoProfile -File scripts/Export-NativeProfileAlternatives.ps1` (PowerShell 7, restored dependencies). Without switches it changes no files; `-Write` regenerates only the three alternative artifacts. It leaves the snapshot/ledger untouched, does not read XML or execute SQL. Both presets are tested over 20 categories with reparsing, token and idempotence checks; final golden/performance/IDE verification remains SC-27.

## CLI: stdin, files, and directories

After building, pass SQL through stdin from the repository root:

```powershell
'select Id from T' | dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore
```

Formatted SQL goes to stdout; `-` can be passed instead of no argument. `--help` shows brief usage. Diagnostics go to stderr. Exit code `0` means formatting succeeded; `2` means a SQL parse, configuration, I/O, or argument error. Code `1` is used only with `--check` when formatting is required. No SQL is printed on a parse error. The CLI uses `Default` unless `--profile` selects `Default`, `Compact`, or `Expanded` (case-insensitive); this works for stdin, one file, and batches. No other settings flags are supported.

To read one `query.sql` file from the current directory:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql
```

The CLI reads one file as UTF-8 (including BOM support), writes formatted SQL to stdout, and does not modify the source file. A missing or unreadable file returns code `2`, writes an error to stderr, and leaves stdout empty. Multiple files without `--check` or `--write` are rejected: there is no combined stdout format.

To write the result back to the same file, add `--write` after its path:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql --write
```

This mode leaves stdout empty, preserves the presence of a UTF-8 BOM, and does not rewrite an already formatted file. Invalid SQL or a write error leaves the source file unchanged, returns code `2`, and writes diagnostics to stderr. `--write` is not available for stdin (`-`).

To check without changing the file, use `--check`:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql --check
$LASTEXITCODE
```

Code `0` means the file is already formatted; `1` means formatting is needed; `2` means a SQL, configuration, read, or argument error. `--check` never writes the file and leaves stdout empty. It is not available for stdin (`-`) yet.

### Batch checking and writing

A directory or multiple files require `--check` or `--write`; without either mode the CLI returns `2`. For example:

```powershell
tsqlformat ./sql --check
tsqlformat first.sql second.sql --write
tsqlformat ./sql --check --exclude generated --exclude reports/legacy.sql
```

Directories are scanned recursively for `.sql` extensions case-insensitively, including hidden files; file and directory links are not followed. An explicitly named file may have any extension. Duplicate and overlapping paths are processed once in sorted order. Repeat `--exclude` only with a directory operand: it is a literal path relative to each specified directory, naming a file or subtree. The named path and everything beneath a named directory are skipped; globs, absolute paths, and `..` are not supported. Path matching is case-insensitive on Windows and case-sensitive elsewhere. A directory with no selected SQL files reports `TSF9000` and makes the overall exit code `2`.

Batch `--check` keeps stdout empty and lists files needing changes on stderr as `Would reformat: path`. The overall exit code is `2` if any path, SQL, or configuration fails; otherwise `1` if any file needs formatting; otherwise `0`. Batch `--write` also keeps stdout empty and handles files independently: an error in one file does not prevent successful files from being written, but the failing file is left untouched. Its overall code is `2` on any error, otherwise `0`; there is no transaction across the batch. Each file uses its own discovered configuration. A single file without a mode still writes its result to stdout.

### Configuration discovery for files

For `query.sql`, including `--write` and `--check`, the CLI starts with the selected built-in profile (`Default` when omitted), overlays `.editorconfig` properties from the SQL file's directory and parents up to `root = true` or the filesystem root, and finally overlays the nearest `.tsqlformatter.json`. A nearer `.editorconfig` and a later matching section take precedence; JSON search stops after the directory containing a `.git` marker or at the filesystem root. JSON properties override `.editorconfig` and the profile by field. A relative SQL path is made absolute from the current directory; parent traversal does not resolve symbolic links. Stdin uses only the selected profile and does not trigger discovery.

For example, `tsqlformat query.sql --profile Expanded` uses one SELECT item per line unless the JSON file overrides `select.columns`. `tsqlformat ./sql --check --profile Compact` applies Compact separately to each selected file before that file's configuration. Unknown profile IDs return `TSF2000`; a missing or repeated `--profile` value returns `TSF9000`, code `2`, and no SQL output. The same resolver is used by CLI, Visual Studio, and SSMS: their baselines differ only when IDE option pages are set. For the same SQL text and effective options, all adapters call the same Core formatter.

The limited `.editorconfig` subset supports `[*]` and `[*.sql]` sections and these properties: `indent_style` (`space`/`tab`), numeric `indent_size` from 0 to 32, `end_of_line` (`lf`/`crlf`/`cr`), and `insert_final_newline` (`true`/`false`). `unset` removes an inherited property. Other sections, properties, and invalid values are ignored; complex globs, `tab_width`, `charset`, and `trim_trailing_whitespace` are not supported. `.editorconfig` is read as UTF-8, up to 1 MiB; read or encoding errors produce `TSF9000`, and an oversized file produces `TSF9001`.

The file is read as UTF-8, validated as version 1, and its settings overlay the selected profile and `.editorconfig`. Invalid configuration produces `TSF2000` and the config path on stderr, returns `2`, prints no SQL, and does not perform `--write`. Read failures produce `TSF9000`. If no configuration exists, the profile and any applicable `.editorconfig` settings apply.

### Large inputs

The CLI limits each SQL or JSON configuration file to 64 MiB and decoded input to 16 Mi UTF-16 code units (stdin has the character limit); `.editorconfig` has a separate 1 MiB limit. Exceeding a limit returns code `2` with `TSF9001` on stderr: no SQL is printed and `--write` does not run. Invalid UTF-8 produces `TSF9000`. The parser still operates on a complete string, so arbitrarily large files are not supported yet.

Ctrl+C requests cancellation, prints `TSF9002` on stderr, and exits with code `130`. If canceled before file replacement, `--write` leaves the original file unchanged and removes its temporary file. ScriptDom parsing is synchronous, so cancellation may be observed only after the current parser call finishes.

## JSON configuration

To read and write settings, reference `src/TSqlFormatter.Configuration/TSqlFormatter.Configuration.csproj`. Version 1 of `.tsqlformatter.json` supports the current options-model sections:

```json
{
  "version": 1,
  "general": { "maxLineLength": 100, "lineEnding": "lf", "finalNewLine": false },
  "indent": { "style": "spaces", "size": 4 },
  "keywords": { "case": "upper" },
  "select": { "columns": "auto" },
  "alignment": { "selectAliases": false, "setAssignments": false, "declareTypes": false },
  "joins": { "clauseNewLine": true, "conditionNewLine": true },
  "where": { "conditionNewLine": true, "booleanOperatorNewLine": true },
  "clauses": { "groupByLayout": "auto", "orderByLayout": "auto" }
}
```

`lineEnding` accepts `lf`, `crlf`, or `cr`; `indent.style` accepts `spaces` or `tabs`; `keywords.case` accepts `upper`, `lower`, or `preserve`; layouts accept `auto` or `onePerLine`. The four JOIN/WHERE line-break fields are booleans. `general.maxLineLength` must be an integer of at least 1, and `indent.size` an integer of at least 0. Missing sections and properties use built-in defaults (or IDE defaults in the VSIX). Serialization writes all supported properties and a final LF.

Configuration version `2` retains the existing sections and adds a `"rules"` object. Seven casing rules are available: `textCase.keyword`, `textCase.builtin`, `textCase.dataType`, `textCase.identifier`, `textCase.variable`, and `textCase.alias` accept `inherit`, `preserve`, `upper`, or `lower`; `textCase.formatQuotedIdentifier` accepts `true` or `false`. For example:

```json
{"version":2,"rules":{"textCase.keyword":"lower","textCase.builtin":"upper","textCase.identifier":"preserve","textCase.formatQuotedIdentifier":false}}
```

For keywords, `inherit` uses the older `keywords.case`; for functions and data types, it preserves the former token behavior (including keyword casing where applicable). Identifiers, variables, and aliases retain their spelling by default, and identifiers in `[]` or `""` stay unchanged even when `textCase.identifier` is set. Enable `textCase.formatQuotedIdentifier` explicitly to change those; name casing can matter under a case-sensitive SQL Server collation. Built-in casing applies to recognized calls from the supported list, not schema-qualified user functions. String literals and comments stay unchanged. Unknown rule keys produce `TSF2000`. Version-1 configurations keep their previous output, and ordinary serialization without new rules still writes version 1. Call `SqlFormatterConfigurationSerializer.SerializeV2(options)` for an explicit migration.

Ten spacing rules are also available in `rules`: `spacing.beforeComma`, `spacing.afterComma`, `spacing.beforeDot`, `spacing.afterDot`, `spacing.beforeScopeResolution`, `spacing.afterScopeResolution`, `spacing.arithmeticOperators`, `spacing.beforeFunctionArguments`, `spacing.withinEmptyFunctionArguments`, and `spacing.withinFunctionArguments`. Each accepts `inherit` (default: retain prior layout), `insert` (exactly one space), or `remove` (no space). For example, `{"version":2,"rules":{"spacing.afterComma":"remove","spacing.beforeFunctionArguments":"insert"}}`. `::` is the scope-resolution operator; the arithmetic rule applies only to binary operators recognized in the syntax tree. These rules do not move tokens across lines or change literals or comments, and are skipped if the result fails reparsing or changes the token sequence.

Vertical lists support `stackedList.commaPlacement` (`inherit`, `leading`, `trailing`) and `stackedList.spaceAfterLeadingComma` (`inherit`, `insert`, `remove`). They only change commas between items already separated by a line break. To see the effect on a `SELECT` list, set `"select":{"columns":"onePerLine"}` alongside `leading`. With the standard space, `leading` produces `a\n    , b`; `remove` produces `a\n    ,b`. General `spacing.*` rules run first, and vertical-list settings take precedence for this comma. Both defaults are `inherit`, leaving prior output unchanged.

`misc.packageDelimiterBlankLine` (default `false`) enables empty lines around a standalone `GO` line; `misc.packageDelimiterBlankLineMode` selects `after` (default), `before`, or `both`. For example, `{"version":2,"rules":{"misc.packageDelimiterBlankLine":true,"misc.packageDelimiterBlankLineMode":"both"}}` inserts one empty line on each side of `GO` if not already present. Strings and comments containing `GO` are not delimiters. Changes require ScriptDom to parse the SQL successfully and are revalidated; the `GO 2` repeat count is not supported by the parser in this mode.

For a simple top-level `SELECT`, version 2 can compact the statement onto one line using `select.singleLine.maxWords` and `select.singleLine.maxCharacters` (objects such as `{"enabled":true,"value":10}`), or the Boolean `select.singleLine.whenFitsMargin`. Any enabled condition may qualify, but the result must always fit `general.maxLineLength`; a query with comments is not collapsed. The expression list has `select.list.breakBeforeFirstColumn` (`inherit`/`always`/`never`), `select.list.stackColumns` (`inherit`/`on`/`off`), and `select.list.stackMode` (`onePerLine`/`auto`, effective with `on`). For example, `{"version":2,"rules":{"select.list.stackColumns":"on","select.list.stackMode":"onePerLine","select.list.breakBeforeFirstColumn":"always"}}`. Threshold-based compactness takes precedence over the vertical list when the statement qualifies.

`select.list.indent` accepts `{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}`. `relative` adds `offset` to the former indentation level; `absolute` sets the level directly; `transparent:true` removes list indentation. With `onNewLineOnly:false`, extra indentation also applies before the first column kept on the `SELECT` line. Defaults retain the previous JSON v1 behavior.

The `SELECT INTO`, `FROM`, and `JOIN`/`APPLY` settings live under `select.into.*`, `select.from.*`, and `select.join.*` in `rules`. Boundaries named `breakBefore`/`breakAfter`, and `onBreakBefore`/`onBreakAfter` for `ON`, accept `inherit` (default), `always`, or `never`. Separate indentation rules `keywordIndent`, `tableIndent`, `listIndent`, `onKeywordIndent`, `onConditionIndent`, and `nestedConditionIndent` use the same `enabled`/`offset`/`onNewLineOnly`/`style`/`transparent` object as `select.list.indent`. For example, `{"version":2,"rules":{"select.from.breakAfter":"always","select.from.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}` moves the first table after `FROM` to a new line and adds one indentation level. These settings are independent of similarly named DML clauses; nested `SELECT` lists choose their source through `subquery.useSelectFormatting`.

`select.from.stackList` accepts `inherit`/`on`/`off`, while `select.from.stackMode` chooses `onePerLine` or `auto` with `on` for comma-separated sources. `select.join.wrapCondition` accepts `inherit`/`none`/`and`/`or`/`both`; `select.join.wrapBeforeOperator` and `select.join.wrapAfterOperator` independently accept `inherit`/`always`/`never` around `AND` and `OR` in `ON`. An explicit operator-side rule takes precedence over the general wrap mode. Rules act only at recognized token boundaries of a top-level `SELECT`; gaps containing comments are not rewritten, and the result is reparsed and compared by token sequence. For `SELECT INTO`, only token boundaries are changed so far; full structural layout of the construct is not claimed. When an SC-07 compactness threshold is met, compactness runs after these breaks and may fold the statement back onto one line.

Top-level `SELECT` now has separate `select.where.*`, `select.having.*`, `select.groupBy.*`, and `select.orderBy.*` settings. Each group has its own `breakBefore`, `breakAfter`, `keywordIndent`, and content indent (`conditionIndent` or `listIndent`); breaks accept `inherit`/`always`/`never`, and indents use the object described above. For example, `{"version":2,"rules":{"select.where.breakAfter":"always","select.having.breakAfter":"never"}}` separates `WHERE` and `HAVING` policies even in one query. `GROUP BY` and `ORDER BY` also have `stackList` (`inherit`/`on`/`off`) and `stackMode` (`onePerLine`/`auto`) for comma-separated items.

`WHERE` and `HAVING` conditions independently offer `wrapCondition` (`inherit`/`none`/`and`/`or`/`both`), `wrapBeforeOperator`, `wrapAfterOperator` (`inherit`/`always`/`never`), and `nestedConditionIndent`. An explicit `AND`/`OR` side rule takes precedence over the general wrap mode. `select.where.*` does not change `HAVING`, or vice versa; the older shared JSON v1 switches still establish the initial layout under `inherit`. As in SC-08, only safe gaps between tokens are changed; comments are retained and output is revalidated. SC-07 one-line compactness runs last.

The remaining top-level `SELECT` version-2 settings are in `select.cte.*`, `select.for.*`, `select.option.*`, and `select.compute.*`. CTEs offer independent breaks after `WITH`, before/after `AS`, and around column-list parentheses; `stackColumns` (`inherit`/`on`/`off`) with `stackMode` (`onePerLine`/`auto`); and local `expressionIndent`, `columnListIndent`, `columnBraceIndent`, and `subqueryBraceIndent`. For example, `{"version":2,"rules":{"select.cte.breakAfterWith":"always","select.cte.stackColumns":"on","select.cte.stackMode":"onePerLine"}}`. `FOR XML` offers `select.for.breakBefore`, `select.for.breakAfterXml`, `keywordIndent`, and `specIndent`; `OPTION` hints offer `select.option.breakBefore`, `breakAfter`, `keywordIndent`, and `hintsIndent`. Breaks accept `inherit`/`always`/`never`, and indents use the object described above. Only recognized gaps are changed, after which SQL is reparsed and checked for the same token sequence.

Legacy `COMPUTE` is supported only by the SQL Server 2008 (`Sql2008`) dialect. For a script containing `COMPUTE`, `Auto` tries the `Sql100` parser if the modern parser rejects it and the entire script is valid under `Sql100`; other `Auto` scripts continue to use `Sql180`. `select.compute.breakBefore`, `breakAfter`, `keywordIndent`, and `expressionIndent` act only through this compatibility path. An explicitly selected modern dialect leaves the source unchanged and reports `TSF3005`. This does not imply that `Auto` supports other legacy constructs.

For subqueries in JSON v2, `subquery.useSelectFormatting` defaults to `true`: nested SELECT lists inherit explicit `select.list.*` overrides. When `false`, those overrides are ignored and the list uses independent `subquery.list.indent`, `breakBeforeFirstColumn`, `stackColumns` (`inherit`/`on`/`off`), and `stackMode` (`onePerLine`/`auto`). Four parenthesis boundaries — `subquery.breakBeforeOpen`, `breakAfterOpen`, `breakBeforeClose`, and `breakAfterClose` — accept `inherit`/`always`/`never`. `subquery.indent` sets the body indent with the `enabled`/`offset`/`onNewLineOnly`/`style`/`transparent` object. These independent rules apply with `useSelectFormatting:false`; otherwise the earlier layout remains. Example:

```json
{"version":2,"rules":{"subquery.useSelectFormatting":false,"subquery.list.stackColumns":"on","subquery.list.stackMode":"onePerLine","subquery.breakAfterOpen":"always"}}
```

Five `subquery.singleLine.*` groups cover `allAnySomeExists`, `cteQueries`, `fromList`, `inOperator`, and `other`. Each has Boolean `any` and `whenFitsMargin`, plus threshold objects `maxWords` and `maxCharacters` such as `{"enabled":true,"value":10}`. One satisfied condition is enough: `any` compacts regardless of the right margin; `whenFitsMargin` requires the line to fit `general.maxLineLength`; thresholds require the word/character count to be **less than** the given value. For example, `{"version":2,"rules":{"subquery.useSelectFormatting":false,"subquery.singleLine.inOperator.whenFitsMargin":true}}`. A subquery with comments is not flattened; if any script token has an embedded newline in a literal or quoted identifier, the entire script is left unchanged to preserve its contents. Changed SQL is reparsed and checked for the same token sequence. Body indent currently applies to space indentation, not tabs.

For a nested query with `subquery.useSelectFormatting:false`, independent groups are available: `subquery.from.*`, `subquery.join.*`, `subquery.where.*`, `subquery.groupBy.*`, `subquery.having.*`, `subquery.orderBy.*`, `subquery.cte.*`, and `subquery.for.*`. Names after the prefix match the `select.*` rules described above: separate `breakBefore`/`breakAfter`, local indents, list stacking, and `AND`/`OR` wrapping. For example, `{"version":2,"rules":{"subquery.useSelectFormatting":false,"subquery.where.breakAfter":"always","subquery.from.stackList":"on","subquery.from.stackMode":"onePerLine"}}`. CTE rules affect its header and column list, while `subquery.for.*` affects valid `FOR XML` in a scalar subquery. `ORDER BY` in a derived subquery needs a valid context such as `TOP`; `OPTION (...)` inside a scalar/derived subquery or CTE definition is not valid T-SQL, by the agreed decision, the eight matching SQL Complete parameters are classified as not applicable to T-SQL, so `subquery.option.*` is not offered. If a nested query body contains a comment, structural layout of that body is conservatively skipped and the original comment is retained.

```csharp
using System.IO;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;

var serializer = new SqlFormatterConfigurationSerializer();
var options = serializer.Deserialize(File.ReadAllText(".tsqlformatter.json"));
var result = new ScriptDomSqlFormatter().Format(
    "select Id from T", options, new FormatRequest());
File.WriteAllText(".tsqlformatter.json", serializer.Serialize(options));
```

This explicitly reads a file in your application. The CLI and VSIX discover configuration automatically for a named SQL file; `ScriptDomSqlFormatter` itself does not. Other integrations can call `new SqlFormatterConfigurationResolver().ResolveForSqlFile("query.sql")` for `Default` or validated settings and error diagnostics. The plan's example also shows future fields (`expressions`, `aliases`, and others); the current serializer rejects them as unknown.

To handle invalid input without an exception, use `Parse`:

```csharp
var parsed = serializer.Parse(File.ReadAllText(".tsqlformatter.json"));
if (!parsed.Succeeded)
{
    foreach (var diagnostic in parsed.Diagnostics)
        System.Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
    return;
}

var validatedOptions = parsed.Options!;
```

Unknown sections and properties, unsupported versions, duplicate keys, wrong types, and invalid values produce `TSF2000` diagnostics with `Error` severity. On any error, `Options` is `null`; independent field errors are collected together. `Deserialize` throws `JsonSerializationException` for invalid configuration. For example, the plan's `lineEnding: "auto"` is not supported yet; use `lf`, `crlf`, or `cr` instead.

### Option precedence

`SqlFormatterConfigurationResolver` combines the selected profile (`Default` by default), an explicitly supplied file's contents, and explicitly supplied options in that order. The final layer replaces only specified fields; other file and profile settings remain intact. Pass `null` for a missing file. Invalid JSON returns diagnostics and no partially applied options.

```csharp
var json = File.ReadAllText(".tsqlformatter.json");
var resolved = new SqlFormatterConfigurationResolver().Resolve(
    json,
    new FormattingOptionsOverrides(maxLineLength: 120, keywordCase: KeywordCase.Lower));
if (!resolved.Succeeded)
    throw new System.InvalidOperationException(resolved.Diagnostics[0].Message);
var effectiveOptions = resolved.Options!;
```

`FormattingOptionsOverrides` parameters map to supported JSON fields, including `maxLineLength`, `lineEnding`, `finalNewLine`, `indentSize`, `useTabs`, `keywordCase`, `selectColumns`, `groupByLayout`, `orderByLayout`, `alignSelectAliases`, `alignSetAssignments`, and `alignDeclareTypes`. This is an application API; the CLI loads a discovered file automatically and accepts `--profile`, but does not accept per-field settings flags.

### Named profiles

`FormattingProfileCatalog` provides `Default` (standard values), `Compact` (line width 120), and `Expanded` (line width 80; `SELECT` columns and `GROUP BY`/`ORDER BY` items one per line). IDs are matched case-insensitively. Select a profile with `profileId`:

```csharp
var projectProfile = new FormattingProfile(
    "project", "Project", FormattingOptions.Default.With(indent: new IndentOptions(2)));
var catalog = new FormattingProfileCatalog(new[] { projectProfile });
var configured = new SqlFormatterConfigurationResolver(catalog).Resolve(
    json, profileId: "project");
```

An application can supply custom user or project profiles when creating the catalog. Duplicate IDs, including collisions with built-ins, are rejected. An unknown `profileId` returns `TSF2000` with `Options == null`. File fields overlay the profile rather than resetting it to `Default`. The CLI and VSIX expose built-in profile selection; the VSIX also supports portable options import/export. Named custom profiles in JSON and custom profile IDs in the CLI or VSIX are not implemented.

To check comment preservation separately, run the golden suite:

```powershell
dotnet test tests/TSqlFormatter.GoldenTests/TSqlFormatter.GoldenTests.csproj --no-restore
```

The suite contains 60 fixed expected-SQL cases with leading, inline, block, and standalone comments. It checks that each comment appears once and that formatting again does not change the result. These checks are also included in `dotnet test TSqlFormatter.sln`.

## Parsing T-SQL

```csharp
using System;
using TSqlFormatter.Core.Parsing;

ISqlParser parser = new ScriptDomSqlParser();
var result = parser.Parse("SELECT 1;", SqlDialectVersion.Auto);

if (result.ParseSucceeded)
{
    // result.Root contains the ScriptDom AST; result.Tokens contains source tokens.
}
else
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Message}");
    }
}
```

`result.Source` retains the input text. `result.Diagnostics` provides the ScriptDom error number, message, offset, line, and column. On failure, `result.Root` may be partial, so use it only after checking `ParseSucceeded`.

`SqlDialectVersion` accepts `Auto`, `Sql2008`, `Sql2016`, `Sql2017`, `Sql2019`, `Sql2022`, and `Latest`. `Sql2008` uses ScriptDom `Sql100`, and `Latest` uses `Sql180`. `Auto` normally uses `Sql180` but can safely fall back to `Sql100` for a parseable `COMPUTE`; it does not detect the server version. `ScriptDomSqlParser` enables quoted identifiers by default; pass `initialQuotedIdentifiers: false` to change the initial setting.

## Navigating tokens and fragments

After a successful parse, create a `SqlTokenNavigator` for the same result:

```csharp
using System;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlFormatter.Core.Parsing;

var result = new ScriptDomSqlParser().Parse("SELECT /* note */ 1;", SqlDialectVersion.Auto);
if (result.ParseSucceeded)
{
    var navigator = new SqlTokenNavigator(result);
    var script = (TSqlScript)result.Root!;
    var statement = script.Batches[0].Statements[0];

    var firstToken = navigator.GetToken(statement.FirstTokenIndex);
    var next = navigator.GetNextMeaningfulToken(statement.FirstTokenIndex);
    var tokens = navigator.GetFragmentTokens(statement);
    var span = navigator.GetTextSpan(statement);
    var originalText = result.Source.Substring(span.StartOffset, span.Length);

    Console.WriteLine($"{firstToken.Text} -> {next?.Text}; tokens: {tokens.Count}");
    Console.WriteLine(originalText);
}
```

`GetToken(index)` returns the token at that exact index in `result.Tokens`. `GetPreviousMeaningfulToken(index)` and `GetNextMeaningfulToken(index)` search strictly before and after the specified token, skipping whitespace, single-line and multiline comments, and the end-of-file marker; they return `null` when no suitable token exists. These methods throw `ArgumentOutOfRangeException` for an index outside the token range.

`GetFragmentTokens(fragment)` returns every token from `FirstTokenIndex` through `LastTokenIndex`, inclusive, preserving comments and whitespace inside the fragment. `GetTextSpan(fragment)` returns a range in the original source: `StartOffset`, `Length`, and exclusive `EndOffset`. Offsets are zero-based .NET character positions. A `null` fragment raises `ArgumentNullException`; a fragment with invalid bounds raises `ArgumentException`. Token navigation remains available when parsing reports errors, but AST fragments may be partial.

## Classifying comments

`SqlTriviaScanner` inspects parse-result tokens without modifying the SQL source:

```csharp
using TSqlFormatter.Core.Parsing;

var parsed = new ScriptDomSqlParser().Parse(
    "SELECT Id, -- note\nName FROM T", SqlDialectVersion.Auto);
var comments = new SqlTriviaScanner().Scan(parsed);
foreach (var comment in comments)
{
    System.Console.WriteLine($"{comment.Placement}: {comment.Text}");
}
```

Each `SqlCommentTrivia` carries an exact `Span`, original `Text`, `Kind` (`Line`/`Block`), `TokenIndex`, and `Placement`: `Trailing` follows a SQL token on the same line; `Leading` is adjacent to the next token (including a contiguous chain of comments); `Standalone` has no such attachment, for example when separated by a blank line. `AnchorTokenIndex` refers to the attached SQL token in `parsed.Tokens`; it is `null` for `Standalone`. The scanner also works with available tokens after a parse error. The formatter uses this classification for line and block comments after commas and before the next column in `SELECT`, and before supported clauses; other placements do not yet have dedicated formatting rules.

## Positions in the original source

Every `SqlParseResult` contains a `LineMap` built from the unchanged source text:

```csharp
using System;
using TSqlFormatter.Core.Parsing;

var result = new ScriptDomSqlParser().Parse("SELECT 1;\r\nSELECT 2;", SqlDialectVersion.Auto);
var offset = result.Source.IndexOf("SELECT 2;", StringComparison.Ordinal);
var position = result.LineMap.GetLinePosition(offset);
Console.WriteLine($"{position.Line}:{position.Column}"); // 2:1
Console.WriteLine(result.LineMap.GetOffset(position)); // original offset
```

`GetLinePosition(offset)` converts an offset to a `SqlLinePosition` with `Line` and `Column` properties; `GetOffset(line, column)` or `GetOffset(position)` performs the reverse conversion. `LineCount` reports the number of lines. Offsets start at 0 and include the position after the last character; lines and columns start at 1. Columns count UTF-16 code units, not visible characters. LF, CRLF, and lone CR line endings are supported. Both code units of CRLF belong to the preceding line, and the next line starts after LF; this makes every offset, including one inside CRLF, round-trip exactly. Empty text has one line with position `1:1`. Invalid offsets and positions raise `ArgumentOutOfRangeException`.

## Layout document model for developers

In `TSqlFormatter.Core.Layout`, you can compose a tree from `TextDoc`, `ConcatDoc`, `SoftLineDoc.Instance`, `HardLineDoc.Instance`, `IndentDoc`, `GroupDoc`, and `IfBreakDoc`. For example:

```csharp
using System;
using TSqlFormatter.Core.Layout;

Doc document = new GroupDoc(new ConcatDoc(new Doc[]
{
    new TextDoc("SELECT"),
    SoftLineDoc.Instance,
    new IndentDoc(1, new TextDoc("Id"))
}));

var rendered = new DocRenderer().Render(
    document,
    new DocRenderOptions(maxLineWidth: 6, finalNewline: true));
Console.Write(rendered); // SELECT\n    Id\n
```

`SoftLineDoc` means a space in flat mode or a newline in broken mode; `HardLineDoc` means an unconditional newline. `GroupDoc` tries to keep content on one line if it fits. `IndentDoc` specifies a nonnegative number of indentation levels, and `IfBreakDoc(broken, flat)` selects an alternative according to the group mode.

By default, `DocRenderOptions` uses a width of 100 UTF-16 code units, 4 spaces per indentation level, LF, and no final newline. You can set `maxLineWidth`, `indentWidth`, `lineEnding` (`Lf`, `CrLf`, `Cr`), `finalNewline`, and `useTabs`. The selected EOL applies to document breaks; line endings inside `TextDoc` are preserved. The renderer removes generated trailing spaces but does not change literal `TextDoc` content. An empty document stays empty even with `finalNewline: true`.

For developers, `SqlLayoutPolicy.Resolve` now merges fields in global → statement → clause → local order, while `SqlLayoutPrimitives` composes breaks, comma-separated lists, parentheses, and safely aligned pairs. Automatic layout requires a `GroupDoc`; unsafe alignment (for example, comments, tabs, or excessive width) returns `null`. These internal primitives do not yet add user-facing switches or change existing output.

## Measuring renderer performance

From the repository root, after `dotnet restore TSqlFormatter.sln`, run BenchmarkDotNet in Release:

```powershell
dotnet run --project benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -c Release --no-restore -- --filter '*DocRendererBenchmarks*'
```

The `SmallFlat`, `MediumWrapped`, and `LargeWrapped` scenarios measure only `DocRenderer.Render` on prebuilt documents with 5, 50, and 500 columns. The report includes time and allocations; it does not measure SQL parsing, tree construction, or end-to-end formatting. Add `--job Dry` for a quick execution check, but do not use its single measurement for performance comparisons. The first run may need NuGet access for BenchmarkDotNet's child project.

## Keyword casing

`ScriptDomSqlFormatter` implements `ISqlFormatter.Format(source, options, request, cancellationToken)`. It changes the case of tokens ScriptDom recognizes as keywords and of supported AST-confirmed `JOIN`/`APPLY` operators; strings, comments, and identifiers are preserved. The default is `Upper`; `Lower` and `Preserve` are also available:

```csharp
using TSqlFormatter.Core.Formatting;

ISqlFormatter formatter = new ScriptDomSqlFormatter();
var result = formatter.Format("select 'from' from dbo.Items",
    new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Upper)),
    new FormatRequest());
System.Console.WriteLine(result.Text); // SELECT 'from' FROM dbo.Items
```

`FormatRequest` defaults to whole-document scope (`Document`), the `Auto` dialect, and strict parse-failure behavior (`Strict`). `Selection` requires a `SqlTextSpan` and formats only the top-level statement containing the selection; unsafe boundaries or multiple statements produce `TSF3003` without changes. For `Statement`, pass the caret as `new SqlTextSpan(offset, 0)` in the `selection` argument: the nearest top-level statement is chosen; an invalid position or no statement produces `TSF3004`, while a missing position produces `TSF3000`. In `Strict` mode, a syntax error leaves the source unchanged and produces no edits: `ParseSucceeded == false` with one or more `TSF1000` diagnostics of `Error` severity. If generated SQL fails re-parsing, the original source is also preserved and `TSF3001` is an error (`ParseSucceeded` still describes the successful parse of the original SQL).

The supplied `cancellationToken` is checked during AST traversal, rendering, and edit application. Cancellation throws `OperationCanceledException` rather than returning a partial `FormatResult`; a synchronous ScriptDom parse call cannot be interrupted midway.

Experimental `Safe` is available through `new FormatRequest(parseFailureBehavior: ParseFailureBehavior.Safe)`. When the whole script fails to parse, it attempts to format only separate statements found in the partial AST and successfully parsed in isolation; invalid statements and the gaps between them remain untouched. The result can have `Changed == true` while `ParseSucceeded == false` and retains `TSF1000` diagnostics; the full script may still be syntactically invalid. For wholly valid SQL, `Safe` acts like `Strict`. The CLI always uses `Strict` and never writes a partial result. `TokenFallback` is not implemented yet: requesting it returns `TSF3002` without changes.

`FormattingOptions` groups settings into `General` (width 100, LF, no final newline), `Indent` (4 spaces, no tabs), `Keywords` (`Upper`), `Select` (`Auto`), `Clauses` (`Auto` for `GROUP BY` and `ORDER BY`), `Joins`, and `Where` (line breaks on). Indentation and EOL apply to supported `SELECT`, `INSERT`, `UPDATE`, `DELETE`, and `MERGE` output; width and list layouts affect supported `SELECT` lists. JOIN/ON and WHERE/HAVING/AND/OR line-break settings apply only to supported constructs. `FormatResult` carries final text, `TextEdit` changes, diagnostics, change status, and parse success.

`FormattingOptions.Default` supplies the same values as `new FormattingOptions()`. `With` creates a new option set by replacing only the supplied sections, leaving the original unchanged:

```csharp
var options = FormattingOptions.Default.With(
    general: new GeneralOptions(maxLineWidth: 120),
    keywords: new KeywordOptions(KeywordCase.Lower));
```

The public sections currently include only options backed by implemented behavior. `CASE` and set-operator rules are available through JSON v2; comment layout and other future rules are not yet part of the API.

## Basic SELECT

For a simple `SELECT` of literals or columns, the formatter normalizes spacing between items, retains names and aliases, and places `FROM` on a separate line:

```csharp
var result = new ScriptDomSqlFormatter().Format(
    "select u.Id as UserId,u.Name from dbo.Users u;",
    new FormattingOptions(), new FormatRequest());
System.Console.WriteLine(result.Text);
// SELECT u.Id AS UserId, u.Name
// FROM dbo.Users u;
```

Structural formatting currently covers simple column lists, one table in `FROM`, and basic `WHERE`, `GROUP BY`, `HAVING`, and `ORDER BY`. Line and block comments after commas in the column list and before clauses are handled separately; for other comments inside a construct or an unsupported clause, original layout is retained, although keyword casing may still change. A trailing semicolon is preserved.

### WHERE conditions

Basic comparisons (`=`, `<>`, `!=`, `<`, `>`, `<=`, `>=`, `!<`, `!>`) are spaced around the operator. `AND` and `OR` conditions start on new lines beneath `WHERE`:

```sql
SELECT Id
FROM Items
WHERE
    Id >= @Min
    AND State = 'open';
```

`IS NULL`, `IS NOT NULL`, `LIKE`, and `NOT LIKE` are supported in the same `WHERE`/`HAVING`/`ON` boolean trees. `LIKE ... ESCAPE` and predicates with comments inside the operator retain their original layout, although keyword casing may change. Internal whitespace in scalar expressions is preserved.

### Nested parentheses

Logical groups made of supported comparisons and `AND`/`OR` can be nested. Parentheses are retained, with an additional indent for their contents:

```sql
WHERE
    (
        A = 1
        OR B = 2
    )
    AND C = 3
```

A derived table can also contain a parenthesized query expression, such as `FROM ((SELECT Id FROM T)) AS d`. Each parenthesis level is emitted as a separate nested block. Other complex query expressions retain their original layout for now.

### GROUP BY, HAVING, and ORDER BY

Basic `GROUP BY` and `ORDER BY` lists use trailing commas between items. `ASC` and `DESC` are preserved. `HAVING` supports the same simple comparisons and `AND`/`OR` as `WHERE`:

```sql
SELECT Category, count(*) AS Total
FROM Sales
GROUP BY Category
HAVING
    count(*) > 1
ORDER BY Total DESC;
```

`QueryClauseOptions.GroupByLayout` and `OrderByLayout` accept `ClauseItemLayout.Auto` (the default) or `OnePerLine`. `Auto` keeps the list on one line if it fits within `GeneralOptions.MaxLineWidth`, otherwise it puts each item on its own line. `OnePerLine` always breaks. For example: `new FormattingOptions(clauses: new QueryClauseOptions(ClauseItemLayout.OnePerLine, ClauseItemLayout.OnePerLine))`. `ROLLUP`/`CUBE` groupings, `ORDER BY ALL`, and `OFFSET/FETCH` are not structurally formatted yet.

### Common table expressions (CTEs)

One or more simple CTEs before the main `SELECT`, `INSERT`, `UPDATE`, or `DELETE` are supported. Each CTE's nested query is indented; an optional column-name list is preserved:

```sql
WITH A (Id) AS (
    SELECT Id
    FROM T
),
B AS (
    SELECT Id
    FROM A
)
SELECT Id
FROM B;
```

CTEs with `XMLNAMESPACES`, nested `WITH`, an unsupported query expression, or a comment between the CTE block and DML retain their original layout for now.

### Subqueries

A simple `SELECT` inside a scalar subquery, derived table, `EXISTS`, `IN`, or `NOT IN` is formatted as a separate indented block. Scalar subqueries are supported in the select list and on either side of a basic comparison; subquery predicates work in supported `WHERE`/`HAVING`/`ON` conditions, including combinations with `AND`/`OR`:

```sql
SELECT Id
FROM T
WHERE
    Id IN (
        SELECT Id
        FROM U
    )
    AND EXISTS (
        SELECT 1
        FROM V
    );
```

A scalar subquery in the select list may have an alias; a derived-table example appears below. Parentheses and original keyword spelling under `KeywordCase.Preserve` are retained. Comments in unsupported positions do not receive structural layout: the formatter keeps the construct's original layout, though keyword casing may still change.

### CASE expressions

Both simple `CASE value WHEN ...` and searched `CASE WHEN condition ...` are formatted branch by branch in the `SELECT` list and as an operand of a basic comparison. `THEN` receives an extra indent; `ELSE` is optional:

```sql
SELECT
    CASE Status
        WHEN 1
            THEN 'New'
        ELSE 'Other'
    END AS Label
FROM T
```

In JSON v2, `case.*` controls boundaries through `breakBeforeCase`, `breakBeforeEnd`, `breakBeforeInput` (simple `CASE` only), `breakBeforeThen`, `breakBeforeWhenElse`, and `breakAfterThenElse`. Values are `inherit` (default), `always`, and `never`. Seven independent indents — `caseIndent`, `codeIndent` (the `THEN`/`ELSE` result), `inputIndent`, `thenKeywordIndent`, `whenExpressionIndent`, `whenKeywordIndent`, and `nestedConditionIndent` — use the `enabled`/`offset`/`onNewLineOnly`/`style`/`transparent` object described for `select.list.indent`. Searched `CASE` conditions support `wrapCondition` (`inherit`/`none`/`and`/`or`/`both`), `wrapBeforeOperator`, and `wrapAfterOperator` (`inherit`/`always`/`never`); an explicit operator-side rule takes precedence. For example:

```json
{"version":2,"rules":{"case.breakAfterThenElse":"always","case.wrapCondition":"and","case.codeIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}
```

Condition and result text, as well as comments, is preserved. A comment between `CASE` parts can prevent structural layout; the new rules only change safe whitespace gaps at recognized tokens and never cross a comment. Output is reparsed and compared by token sequence. Numeric modes from the source XML are neither imported nor treated as JSON values.

### Set operators

`UNION`, `UNION ALL`, `INTERSECT`, and `EXCEPT` go on their own lines between supported `SELECT` queries. They can be chained, used in a derived table, and followed by a final `ORDER BY`:

```sql
SELECT Id
FROM A
UNION ALL
SELECT Id
FROM B
ORDER BY Id;
```

The JSON v2 `setOperator.*` group independently controls `breakBefore` (before `UNION`/`EXCEPT`/`INTERSECT`) and `breakAfter` (before the right branch, after `ALL` for `UNION ALL`), with `inherit`/`always`/`never` values. `keywordIndent` and `branchIndent` use the same indent object as `select.list.indent`. The rules work for chains and nested queries. For example, `{"version":2,"rules":{"setOperator.breakAfter":"never","setOperator.keywordIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}`. A comment between a query and the operator is preserved; the adjacent gap is not rewritten, though other safe boundaries may change. Combining such an expression with a CTE or `OFFSET/FETCH` does not yet receive structural formatting.

### Window functions

For a function call directly in the `SELECT` list, an `OVER` clause with `PARTITION BY` and/or `ORDER BY` is split across lines. Partition and sort lists retain their order; empty `OVER()` becomes `OVER ()`:

```sql
SELECT
    row_number() OVER (
        PARTITION BY Category
        ORDER BY CreatedAt DESC
    ) AS rn
FROM T
```

The function arguments retain their original text. Simple `ROWS`/`RANGE` frames with `BETWEEN` bounds (unbounded, current row, or an integer offset) or one such bound are placed on a separate line after `ORDER BY`. Frame comments, complex bounds, named windows, and window functions embedded in more complex scalar expressions retain their original layout, though recognized keywords may change case.

### FROM source

Table names are supported, including `schema.table` and bracket-quoted parts, along with aliases with or without `AS`. A basic derived table containing a `SELECT` is formatted as an indented nested query:

```sql
SELECT d.Id
FROM (
    SELECT Id
    FROM dbo.Items
) AS d;
```

A derived table's column alias list (`AS d(Id)`) is not structurally formatted yet: its original layout is kept and only keyword casing may change.

### JOIN and APPLY

`INNER JOIN` (and short `JOIN`), `LEFT [OUTER] JOIN`, `RIGHT [OUTER] JOIN`, `FULL [OUTER] JOIN`, `CROSS JOIN`, `CROSS APPLY`, and `OUTER APPLY` are supported. Each join in a chain starts on a new line. Its `ON` condition gets its own indented line; basic comparisons and logical groups are formatted, while more complex expressions are kept as they are:

```sql
SELECT a.Id
FROM dbo.A a
INNER JOIN dbo.B b
    ON a.Id = b.Id
LEFT JOIN dbo.C c
    ON b.Id = c.Id;
```

The right side of `APPLY` may be a simple derived table. Join hints such as `HASH JOIN` and other unsupported forms keep their original layout. Tokens recognized by ScriptDom as keywords may still change case.

### Column layout

`SelectOptions.ColumnLayout` accepts `Auto` (the default) or `OnePerLine`. In `Auto`, the entire list stays on one line if it fits within `GeneralOptions.MaxLineWidth`; otherwise every column moves to an indented line. `OnePerLine` always puts each column on its own line. Commas remain after each column except the last:

```csharp
var options = new FormattingOptions(
    select: new SelectOptions(SelectColumnLayout.OnePerLine));
var result = new ScriptDomSqlFormatter().Format(
    "select Id,Name from Users", options, new FormatRequest());
System.Console.WriteLine(result.Text);
// SELECT
//     Id,
//     Name
// FROM Users
```

A line comment immediately after a comma stays with the preceding column. In this case, the list is forced to one column per line even with `Auto`. For example, `select Id, -- note\nName from T` becomes:

```sql
SELECT
    Id, -- note
    Name
FROM T
```

Multiple such comments in one list are supported. Spacing before `--` is normalized to one space and line endings follow `GeneralOptions.LineEnding`; the comment text itself is unchanged. Adjacent comments after a comma and immediately before the next column also stay with that column:

```sql
SELECT
    Id, -- separator
    -- export field
    Name
FROM T
```

If a blank line separates the comments or the comma from a leading comment, or several block comments on one line make attachment ambiguous, the list retains its original layout; recognized keyword casing may still change. This rule does not apply to comments inside column expressions.

### Leading comments before clauses

One or more adjacent line or block comments immediately before `FROM`, `WHERE`, `GROUP BY`, `HAVING`, or `ORDER BY` stay on their own lines before that clause:

```sql
SELECT Id
FROM T
-- filter
WHERE
    Id = 1
```

A comment before `SELECT` also stays in place. Comments inside expressions and comments after code on the same line, apart from supported column-list commas, retain the clause's original layout for now.

### Block comments

A `/* comment */` after a comma in the column list stays with the preceding column, just like a line comment:

```sql
SELECT
    Id, /* label */
    Name
FROM T
```

A block comment before a clause, such as `/* filter */` before `WHERE`, is also retained during formatting. Multiline block-comment text, including its internal line endings, remains unchanged. In other positions the formatter retains the original layout instead of restructuring the construct.

## INSERT

`INSERT [INTO] table [(columns)] VALUES` with one or more rows and `INSERT [INTO] table [(columns)] SELECT` are supported. By default, target columns are normalized with comma-space separators, and each `VALUES` row gets its own line:

```sql
INSERT INTO dbo.T (Id, Name)
VALUES
    (1, 'a'),
    (2, 'b');
```

In `INSERT ... SELECT`, the nested query uses the supported `SELECT` rules and the inheritance choice `subquery.useSelectFormatting`. Column and value order is retained. A supported CTE before `INSERT` is formatted above the statement.

JSON v2 provides 38 `insert.*` rules. Boundaries accept `inherit` (default), `always`, and `never`; local indents use the `enabled`/`offset`/`onNewLineOnly`/`style`/`transparent` object described for `select.list.indent`.

| Group | Line boundaries | Indents |
|---|---|---|
| `insert.into.*` | `breakBefore`, `breakBeforeTable` | `keywordIndent`, `tableIndent` |
| `insert.columns.*` | `breakBeforeOpen`, `breakAfterOpen`, `breakBeforeClose` | `listIndent`, `braceIndent` |
| `insert.values.*` | `breakBeforeKeyword`, `breakAfterKeyword`, `breakAfterOpen`, `breakBeforeClose` | `keywordIndent`, `listIndent`, `braceIndent` |
| `insert.output.*` | `breakBefore`, `breakAfter` | `keywordIndent`, `listIndent` |
| `insert.source.*` | `breakBefore` | `indent` |

`insert.columns.spaceBeforeOpen`, `insert.columns.spaceWithin`, `insert.values.spaceAfterKeyword`, and `insert.values.spaceWithin` accept `inherit`/`insert`/`remove`. They only affect inline gaps; a specified newline takes precedence. The `columns`, `values`, and `output` groups each have independent `stackList` (`inherit`/`on`/`off`) and `stackMode` (`onePerLine`/`auto`). `onePerLine` separates items with newlines; `auto` keeps the list compact when its normalized text plus anchor indentation fits the right margin. Separate `insert.values.stackRows` and `stackRowsMode`, with the same values, control separators between `VALUES` rows independently of expressions within each row. These lists respect the shared `stackedList.commaPlacement` leading-comma setting. `output` rules affect the `OUTPUT` projection, including before `OUTPUT INTO`; destination columns in `OUTPUT INTO` are not part of the `INSERT` target-column list.

Keyword and brace indents are relative to the `INSERT` line, while item indents are relative to the opening-brace or `OUTPUT` line; `absolute` sets the level directly. With `onNewLineOnly:false`, indentation can also add spaces within a line. `insert.source.indent` shifts the complete query body when `SELECT` starts on a new line, retaining its internal layout; for `SELECT` on the header line with `onNewLineOnly:false`, spaces are added before it. Local indents are emitted as spaces.

The `SELECT` source has four one-line conditions: `insert.source.singleLine.any`, `whenFitsMargin` (Booleans), and `maxWords` and `maxCharacters` (threshold objects such as `{"enabled":true,"value":10}`). Any satisfied condition qualifies. `any` compacts regardless of width; `whenFitsMargin` includes indentation, a header on the same line, and the suffix. Thresholds require the word/character count to be strictly less than the configured value. Source compactness runs after its other rules; a query containing comments is not flattened.

For example, save this `.tsqlformatter.json` next to the SQL file and format it with the usual CLI command:

```json
{
  "version": 2,
  "rules": {
    "insert.columns.breakAfterOpen": "always",
    "insert.columns.breakBeforeClose": "always",
    "insert.columns.stackList": "on",
    "insert.columns.listIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
    "insert.values.breakAfterKeyword": "always",
    "insert.values.braceIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
    "insert.values.stackList": "off",
    "insert.values.stackRows": "on"
  }
}
```

The `insert.*` rules apply to a parsed `INSERT` with a named or variable table target and a `VALUES`/`SELECT` source, including statements in stored code. `INSERT ... EXEC` and `DEFAULT VALUES` retain their original layout. Comments and literals are not rewritten; gaps adjacent to comments are skipped, while other safe boundaries may change. Changes are reparsed and checked for the same tokens. SQL Complete profile numeric modes are not imported. Without explicit overrides, JSON v1 and the earlier output are retained.

## UPDATE

A basic `UPDATE` with ordinary `SET` assignments and optional `FROM` and `WHERE` is formatted clause by clause. By default, each assignment gets its own line; `FROM` supports the same simple tables and joins as `SELECT`:

```sql
UPDATE t
SET
    Name = 'x',
    Count = 2
FROM dbo.T t
WHERE
    t.Id = 1;
```

Assignment order is retained. A supported CTE before `UPDATE` is formatted above the statement. Compound assignments such as `+=` and `TOP` do not receive structural formatting by default; explicit `update.*` rules described below can change their safe AST boundaries and lists without rewriting expressions.

## DELETE

A simple `DELETE FROM table [WHERE ...]` and alias-targeted deletion with `FROM` and `JOIN` are supported. `FROM` and `WHERE` start on new lines, and the `WHERE` condition is indented:

```sql
DELETE t
FROM dbo.T t
JOIN dbo.U u
    ON t.Id = u.Id
WHERE
    u.Flag = 1;
```

`DELETE TOP (integer)` and a supported CTE before `DELETE` are formatted structurally. Other `TOP` expressions and comments inside the `DELETE` header retain their original layout by default; explicit `delete.*` rules can change other safe gaps. Recognized keywords may change case.

### Flexible UPDATE and DELETE settings (JSON v2)

There are 45 `update.*` rules and 41 `delete.*` rules. In the table, `p` means `update` or `delete`. Breaks accept `inherit` (default), `always`, `never`. Indents use an `enabled`/`offset`/`onNewLineOnly`/`style`/`transparent` object, like `select.list.indent`; they are disabled by default.

| Group | Break boundaries | Indents |
|---|---|---|
| `p.target.*` | `breakBefore` (before the target table) | `indent` |
| Additional `delete.target.*` | `breakBeforeFrom` (optional header FROM) | `fromKeywordIndent` |
| `update.set.*` | `breakBefore`, `breakAfter` | `keywordIndent`, `listIndent` |
| `p.from.*` | `breakBefore`, `breakAfter` | `keywordIndent`, `listIndent` |
| `p.join.*` | `breakBefore`, `breakAfter`, `onBreakBefore`, `onBreakAfter` | `keywordIndent`, `tableIndent`, `onKeywordIndent`, `onConditionIndent`, `nestedConditionIndent` |
| `p.where.*` | `breakBefore`, `breakAfter` | `keywordIndent`, `conditionIndent`, `nestedConditionIndent` |
| `p.output.*` | `breakBefore`, `breakAfter` | `keywordIndent`, `listIndent` |
| `p.option.*` | `breakBefore`, `breakAfter` (before the opening parenthesis) | `keywordIndent`, `hintsIndent` |

`update.set`, `p.from`, and `p.output` have independent `stackList` (`inherit`/`on`/`off`) and `stackMode` (`onePerLine`/`auto`) rules. `onePerLine` breaks before each subsequent item; `auto` keeps the list compact when the normalized clause text with its anchor indentation fits `general.maxLineLength`. The mode does not set the boundary before the first item: use `breakAfter`. Lists honor `stackedList.commaPlacement` and `spaceAfterLeadingComma`. `alignment.setAssignments` is retained for supported vertical simple assignments; horizontal SET (`off` or a fitting `auto`) removes alignment padding before `=`. Earlier structural alignment restrictions still apply.

`p.from.useSelectFormatting` is a Boolean switch (default `false`). When `true`, `select.from.*` and `select.join.*` replace local `p.from.*`/`p.join.*` rules; local overrides are ignored. Target, SET, WHERE, OUTPUT, and OPTION do not inherit SELECT. When `false`, UPDATE, DELETE, and SELECT are independent. This inheritance does not change rules inside subqueries; those still use `subquery.useSelectFormatting`.

`p.join` and `p.where` offer `wrapCondition` (`inherit`/`none`/`and`/`or`/`both`), `wrapBeforeOperator`, and `wrapAfterOperator` (`inherit`/`always`/`never`). The condition mode inserts a break before selected AND/OR operators and keeps the right operand inline unless that operator side is explicitly set. Nested parentheses and NOT are handled; AND within BETWEEN is not a Boolean-list boundary.

Target and clause keyword indents are relative to the UPDATE/DELETE line; SET/FROM/OUTPUT items to their keyword line, JOIN tables to the JOIN line, ON to the JOIN line, and ON conditions to the ON line. Nested operand indents use the ON/WHERE line, so they do not accumulate on repeated formatting. `option.hintsIndent` indents the parenthesis block and hints starting on a new line. With a disabled local indent, list stacking retains the first item's level. `offset` is measured in `indent.size` units, with negative resulting widths clamped to zero; `relative` and `anchor` use the anchor line, `absolute` sets a level from the start of the line, and `transparent:true` removes indentation. `onNewLineOnly:false` also adds spaces within a line. Local indentation uses spaces.

Save this in `.tsqlformatter.json`, then run `dotnet run --project src/TSqlFormatter.Cli -- query.sql`:

```json
{
  "version": 2,
  "rules": {
    "update.set.breakAfter": "never",
    "update.set.stackList": "off",
    "update.where.breakBefore": "always",
    "update.where.breakAfter": "always",
    "update.where.conditionIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
    "delete.from.useSelectFormatting": true,
    "select.join.onBreakAfter": "always",
    "delete.output.breakAfter": "always",
    "delete.output.stackList": "on"
  }
}
```

For `UPDATE dbo.T SET a = 1, longer = 2 WHERE a = 1;`, the result is:

```sql
UPDATE dbo.T
SET a = 1, longer = 2
WHERE
    a = 1;
```

Rules apply to parsed UPDATE/DELETE statements, including stored code, TOP, variable table targets, JOIN/APPLY, and nested joins. `delete.target.*` controls the `DELETE [FROM] target` header, while `delete.from.*` controls the separate source FROM. `where.*` can change the boundary before `CURRENT OF` but does not rewrite the cursor. `output.*` controls the OUTPUT projection, including OUTPUT INTO and both OUTPUT clauses of one statement; INTO target columns are not part of that projection. OPTION retains hints and their order. Comments and literals are not rewritten; gaps adjacent to comments are skipped, while other safe boundaries may change. Edits are checked by reparsing and comparing tokens. Multiline literals/quoted identifiers block rewriting of the entire script. Invalid SQL stays unchanged. XML numeric modes are not imported; without new overrides, JSON v1 and earlier output are retained. Use VS/SSMS All settings or a configuration file.

## OUTPUT

By default, in supported `INSERT`, `UPDATE`, and `DELETE`, the `OUTPUT` clause starts on its own line. Its projection list uses comma-space separators; `OUTPUT ... INTO table [(columns)]` is also supported. The `insert.output.*`, `update.output.*`, and `delete.output.*` rules described above can override that layout:

```sql
DELETE FROM T
OUTPUT deleted.Id INTO dbo.Audit (Id)
WHERE
    Id = 1;
```

Original expressions and their order are retained. A comment between `OUTPUT` items or an unsupported `INTO` target prevents structural rewriting of the statement; explicit rules can change other safe gaps. Supported `MERGE` statements can also use `OUTPUT` or `OUTPUT ... INTO`.

## MERGE

A basic `MERGE` with named target and source tables is split into `MERGE INTO`, `USING`, `ON`, and `WHEN ... THEN` branches. `UPDATE SET`, `INSERT ... VALUES`, and `DELETE` actions are supported, along with optional `OUTPUT`. A terminating semicolon is required:

```sql
MERGE INTO dbo.Target AS t
USING dbo.Source AS s
ON t.Id = s.Id
WHEN MATCHED THEN
    UPDATE SET
        t.Name = s.Name
WHEN NOT MATCHED THEN
    INSERT (Id, Name)
    VALUES
        (s.Id, s.Name);
```

`WHEN NOT MATCHED BY SOURCE THEN DELETE` is also supported. Additional `AND` branch conditions, compound assignments, a CTE before `MERGE`, and sources unsupported by the structural builder retain their original layout by default; explicit header/source rules below can change safe gaps. Recognized keywords may still change case.

### MERGE header and source (JSON v2)

There are 50 `merge.*` rules for INTO, the target's WITH hints, USING, source joins, source VALUES, and the main ON. Breaks accept `inherit` (default), `always`, `never`. Indents use an `enabled`/`offset`/`onNewLineOnly`/`style`/`transparent` object, like `select.list.indent`, and are disabled by default.

| Group | Break boundaries | Indents |
|---|---|---|
| `merge.into.*` | `breakBefore`, `breakBeforeTable` | `keywordIndent`, `tableIndent` |
| `merge.hints.*` (target WITH) | `breakBefore`, `breakBeforeOpen`, `breakAfterOpen`, `breakBeforeClose` | `keywordIndent`, `braceIndent`, `listIndent` |
| `merge.using.*` | `breakBefore`, `breakAfter` | `keywordIndent` |
| `merge.join.*` (source) | `breakBefore`, `breakAfter`, `onBreakBefore`, `onBreakAfter` | `keywordIndent`, `tableIndent`, `onKeywordIndent`, `onConditionIndent`, `nestedConditionIndent` |
| `merge.on.*` (target/source matching) | `breakBefore`, `breakAfter` | `keywordIndent`, `conditionIndent`, `nestedConditionIndent` |
| `merge.values.*` (USING source) | `breakBeforeKeyword`, `breakAfterKeyword`, `breakAfterOpen`, `breakBeforeClose` | `keywordIndent`, `braceIndent`, `listIndent` |

`merge.into.breakBefore` only affects an existing INTO: the formatter does not add the optional keyword. `merge.using.breakAfter` sets a boundary before the table or opening parenthesis of the source. USING supports named/variable tables, subqueries, JOIN/APPLY, parenthesized joins, and VALUES derived tables. An inner SELECT retains its existing `subquery.*` policies; `merge.join.*` rules do not descend into it.

`merge.join.useSelectFormatting` is a Boolean switch, default `false`. When `true`, `select.join.*` settings replace local `merge.join.*` settings, which are ignored. This only affects source joins: USING, VALUES, the main `merge.on.*`, and subqueries remain independent. `merge.join` and `merge.on` offer `wrapCondition` (`inherit`/`none`/`and`/`or`/`both`), `wrapBeforeOperator`, and `wrapAfterOperator` (`inherit`/`always`/`never`). The condition mode breaks before selected AND/OR operators and keeps the right operand inline unless that side is overridden. Nested parentheses and NOT are handled; AND in BETWEEN is not broken as a Boolean operator.

`merge.hints.spaceBeforeOpen`, `merge.hints.spaceWithin`, `merge.values.spaceAfterKeyword`, and `merge.values.spaceWithin` accept `inherit`/`insert`/`remove` and only change inline gaps. Explicit line breaks take precedence. WITH hints retain their order, text, and nested parentheses, such as `INDEX(ix_a, ix_b)`; `hints.listIndent` applies to the first hint and subsequent hints already starting on a new line, but there is no separate vertical hint-list switch yet.

`merge.values.stackList` and `stackMode` control expressions within each row; `stackRows` and `stackRowsMode` control gaps between rows. Both lists offer `inherit`/`on`/`off` and `onePerLine`/`auto`. `auto` measures the prospective compact normalized form with configured parenthesis boundaries, spaces, and anchor indentation, and keeps the list compact when that form fits `general.maxLineLength`. Extra padding before managed commas does not change the decision; lists containing comments are conservatively treated as not fitting. Boundaries before the first item are set separately. Vertical output honors `stackedList.commaPlacement` and `spaceAfterLeadingComma`. Parentheses controlled by `values.breakAfterOpen`/`breakBeforeClose` belong to an individual VALUES row, not the outer derived-table wrapper. The source alias and its column list are not rewritten. VALUES in branch INSERT actions and inside subqueries are outside these rules.

INTO, target, WITH, hint parentheses, USING, JOIN, and main ON indents use the MERGE line; hints use the opening parenthesis line, JOIN tables use the JOIN line, join ON uses the JOIN line, and its condition/nested operands use that ON line. The main condition and its nested operands use the main ON line. The VALUES keyword and its row parentheses use the USING line; expressions use their row's opening parenthesis line. With a disabled local indent, list stacking retains the first item's level. `offset` is measured in `indent.size` units, with resulting widths clamped to zero. `relative`/`anchor` use the anchor line, `absolute` sets a level from the start of the line; `transparent:true` removes indentation, and `onNewLineOnly:false` also adds spaces within a line. Local indents use spaces and do not accumulate on repeated formatting.

Save this in `.tsqlformatter.json` and run `dotnet run --project src/TSqlFormatter.Cli -- query.sql`:

```json
{
  "version": 2,
  "rules": {
    "merge.into.breakBefore": "always",
    "merge.using.breakBefore": "always",
    "merge.on.breakBefore": "always",
    "merge.on.breakAfter": "always",
    "merge.on.conditionIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
    "merge.values.breakAfterKeyword": "always",
    "merge.values.braceIndent": {"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
    "merge.values.stackRows": "on",
    "merge.values.stackList": "off"
  }
}
```

For a single-line `MERGE INTO dbo.T AS t USING (VALUES (1, 'x'), (2, 'y')) AS s(id, a) ON t.id = s.id WHEN MATCHED THEN DELETE;`, the result is:

```sql
MERGE
INTO dbo.T AS t
USING (VALUES
    (1, 'x'),
    (2, 'y')) AS s(id, a)
ON
    t.id = s.id WHEN MATCHED THEN DELETE;
```

In this example, the branch stays on the condition's line: header settings do not control WHEN/THEN or actions — use the separate branch rules below. Use the SC-18 rules below for OUTPUT/OPTION/TOP. Header/source settings also work with CTEs, TOP, additional branch conditions, and stored code, without changing expressions or action order. Comments and literals are not rewritten; gaps adjacent to comments are skipped, while other safe boundaries may change. Edits are checked by reparsing and comparing tokens. Multiline literals/quoted identifiers block rewriting of the entire script; invalid SQL stays unchanged. XML numeric modes are not imported; JSON v1 and earlier output without new overrides are retained. Use VS/SSMS All settings or a configuration file.

## MERGE branches (SC-17)

JSON v2 exposes 40 independent branch rules. Header rules `merge.into/using/on/join/values` remain separate: `merge.values` affects only the USING source, whereas `merge.insert.values` affects the INSERT action.

| Rule group | Key suffixes |
| --- | --- |
| `merge.when` | `keywordIndent, conditionIndent, nestedConditionIndent, breakBefore, breakAfter, wrapCondition, wrapBeforeOperator, wrapAfterOperator` |
| `merge.then` | `keywordIndent, actionIndent, breakBefore, breakAfter` |
| `merge.update.set` | `keywordIndent, listIndent, breakBefore, breakAfter, stackList, stackMode` |
| `merge.insert.columns` | `listIndent, braceIndent, breakBeforeOpen, breakAfterOpen, breakBeforeClose, spaceBeforeOpen, spaceWithin, stackList, stackMode` |
| `merge.insert.values` | `keywordIndent, listIndent, braceIndent, breakBeforeKeyword, breakAfterKeyword, breakAfterOpen, breakBeforeClose, spaceAfterKeyword, spaceWithin, stackList, stackMode` |

Breaks: `inherit/always/never`; spaces: `inherit/insert/remove`; lists: `inherit/on/off`, mode `onePerLine/auto`. `auto` compares the normalized compact token width of the list with MaxLineWidth, including anchor indentation and comma spacing; comments within a list disable automatic compaction. Global leading-comma settings apply. Conditions: `inherit/none/and/or/both`; the AND joining MATCHED to its extra condition is not controlled here, only logical operations inside that condition. `conditionIndent` indents MATCHED/NOT MATCHED following WHEN.

Indents use the common `enabled/offset/onNewLineOnly/style/transparent` object. Relative anchors are MERGE (WHEN), WHEN (THEN), THEN (action), the action (SET/INSERT), or the opening brace (items). Absolute indentation ignores the anchor; transparent indentation is zero.

`merge.update.useStatementFormatting` and `merge.insert.useStatementFormatting` (boolean, false by default) select `update.set.*` and `insert.columns/values.*` instead of local rules. INSERT inherits the single VALUES list rules, not standalone INSERT row layout.

Example `.tsqlformatter.json` used by CLI and adapters:

```json
{"version":2,"rules":{
  "merge.when.breakBefore":"always","merge.then.breakBefore":"always","merge.then.breakAfter":"always",
  "merge.then.actionIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
  "merge.update.set.stackList":"on","merge.insert.columns.stackList":"off","merge.insert.values.stackList":"off"
}}
```

UPDATE/INSERT/DELETE branch order, tokens, comments and literal contents are preserved. DELETE uses the shared THEN action indent. Without new overrides, including JSON v1, previous output is unchanged. Invalid SQL and scripts containing multiline literals are not rewritten; gaps adjacent to comments are skipped. OUTPUT/OPTION/TOP settings are described in the SC-18 section below. Use VS/SSMS All settings or a configuration file.

## TOP, OUTPUT and OPTION in MERGE (SC-18)

JSON v2 completes MERGE settings with these keys:

| Group | Suffixes |
| --- | --- |
| `merge.top` | `keywordIndent, percentIndent, breakBefore, breakBeforeOpen, breakAfterOpen, breakBeforeClose, breakBeforePercent, spaceAfterKeyword, spaceWithin` |
| `merge.output` | `keywordIndent, listIndent, breakBefore, breakAfter, stackList, stackMode` |
| `merge.option` | `keywordIndent, hintsIndent, breakBefore, breakAfter` |

Types and modes match the branch rules above. TOP and OUTPUT/OPTION indent from MERGE; PERCENT from TOP; OUTPUT items and hints from their keyword. `option.breakAfter` breaks before the opening parenthesis following OPTION; `hintsIndent` also applies to already-wrapped hints. OUTPUT and OUTPUT INTO share settings, but the destination table/columns are outside the output list. TOP edits only the outer expression parentheses, not nested parentheses; PERCENT is independent. Syntax rejected by the parser, such as MERGE TOP WITH TIES, stays unchanged.

Example configuration:

```json
{"version":2,"rules":{"merge.top.breakBefore":"always","merge.top.spaceWithin":"remove",
"merge.output.breakBefore":"always","merge.output.stackList":"on","merge.option.breakBefore":"always"}}
```

All 109 groups/181 scalar paths in the MERGE section have native counterparts. This is capability coverage, not a promise of byte-for-byte SQL Complete output; XML numeric modes still are not imported. Defaults and v1 output remain unchanged. Comment/literal safety, reparsing and token checks apply as described above. New settings are available through IDE All settings and JSON.

## DECLARE: variables and cursors (SC-19)

JSON v2 exposes:

| Group | Key suffixes |
| --- | --- |
| `declare.variables` | `listIndent, tableIndent, breakAfter, breakBeforeTable, stackList, stackMode` |
| `declare.cursor` | `keywordIndent, forIndent, queryIndent, breakBefore, breakBeforeFor, breakBeforeQuery` |
| `declare.cursor.singleLine` | `any, whenFitsMargin, maxWords, maxCharacters` |

`breakAfter` breaks before the first variable following DECLARE (including table variables); `breakBeforeTable` controls TABLE. `stackList/stackMode` control variable lists: inherit/on/off and onePerLine/auto, including global leading commas. Vertical type alignment is preserved; compact lists remove extra alignment padding.

Indents use the common enabled/offset/onNewLineOnly/style/transparent object, anchored at DECLARE. `queryIndent` shifts every cursor SELECT line while preserving relative internal indentation. Inline OnNewLineOnly=true adds nothing; false adds local spaces.

Other breaks use inherit/always/never. Cursor compaction is enabled by any independent condition: `any` (boolean), `whenFitsMargin` (boolean), or `maxWords/maxCharacters` thresholds such as `{"enabled":true,"value":100}`. Thresholds are strict (less than the value); margin checks include the current line prefix and suffix. Comments prevent query compaction. Table-variable column structure is currently preserved.

Verified `.tsqlformatter.json` example:

```json
{"version":2,"rules":{"declare.variables.breakAfter":"always","declare.variables.stackList":"on",
"declare.variables.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
"declare.cursor.breakBeforeFor":"always","declare.cursor.breakBeforeQuery":"always"}}
```

Rules also apply inside stored code; variable order, values, cursor options and comments are preserved. Defaults/v1 retain previous behavior; invalid SQL and multiline literals are not rewritten. XML numeric Style is not interpreted. New keys are available through VS/SSMS All settings or JSON.

## Code, blocks and transactions (SC-20)

JSON v2 provides independent settings:

| Group | Key suffixes |
| --- | --- |
| `code` | `separateStatements, breakAfterBegin, breakBeforeEnd` |
| `code.block` | `bodyIndent, blankLinesAround, breakBeforeCatch` |
| `code.transaction` | `bodyIndent` |
| `code.if` | `keywordIndent, bodyIndent, conditionIndent, nestedConditionIndent, blankLinesAround, breakAfterCondition, breakBeforeElse, breakAfterElse, wrapCondition, wrapBeforeOperator, wrapAfterOperator` |
| `code.while` | `keywordIndent, bodyIndent, conditionIndent, nestedConditionIndent, blankLinesAround, breakAfterCondition, wrapCondition, wrapBeforeOperator, wrapAfterOperator` |

Breaks use inherit/always/never. `separateStatements` acts between sibling statements in a list, not inside expressions. `blankLinesAround` is boolean: false preserves existing blank lines; true adds at least one before/after the construct when a neighboring statement exists (not around ELSE/END/GO). Gaps adjacent to comments are skipped.

Indents use the common enabled/offset/onNewLineOnly/style/transparent type. `keywordIndent` positions BEGIN/END relative to IF/WHILE; `bodyIndent` shifts the entire body relative to BEGIN, or a single statement relative to IF/WHILE. Conditions anchor to IF/WHILE; conditionIndent retains an existing break after the keyword, but does not create one. OnNewLineOnly=false allows inline padding. Nested bodies are processed outermost first and may use different styles. Unconditional BEGIN/END and TRY/CATCH use `code.block.bodyIndent`; IF/WHILE blocks use their own settings. `breakBeforeCatch` controls BEGIN CATCH after END TRY. Boolean condition modes are inherit/none/and/or/both; BETWEEN and literal contents are preserved.

Transaction bodies are formatted only for lexically closed BEGIN TRANSACTION and COMMIT/ROLLBACK pairs in the same statement list. Nested pairs receive nested indents; unmatched pairs, transactions across branches and SAVE TRANSACTION are not interpreted as bodies. This is a layout rule, not execution or active-transaction-count analysis.

Verified configuration:

```json
{"version":2,"rules":{"code.separateStatements":"always","code.breakAfterBegin":"always","code.breakBeforeEnd":"always",
"code.if.breakAfterCondition":"always","code.if.bodyIndent":{"enabled":true,"offset":2,"onNewLineOnly":true,"style":"relative","transparent":false},
"code.while.bodyIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}
```

Works in Core/CLI and via IDE configuration files; selection formatting preserves neighboring text. New rules are opt-in, with previous v1/default output retained. With Code rules enabled, a trailing TRY/CATCH semicolon is preserved (the legacy structural mode without these rules could omit it). Reparsing checks tokens; invalid SQL/multiline literals remain unchanged. XML numeric modes are not guessed.

## Procedures, functions and views (SC-21)

JSON v2 settings apply to CREATE, ALTER and CREATE OR ALTER:

| Group | Key suffixes |
| --- | --- |
| `routine.parameters` | `listIndent, braceIndent, breakBeforeOpen, breakAfterOpen, breakBeforeClose, spaceBeforeOpen, spaceWithin, spaceWithinEmpty, stackList, stackMode` |
| `routine.with` | `keywordIndent, listIndent, breakBefore, breakAfter, stackList, stackMode` |
| `routine.returns` | `tableIndent, breakBefore, breakBeforeTable` |
| `routine.body` | `asIndent, keywordIndent, codeIndent, breakBeforeAs, breakBefore` |
| `view.columns` | `listIndent, braceIndent, breakBeforeOpen, breakAfterOpen, breakBeforeClose, spaceBeforeOpen, spaceWithin, stackList, stackMode` |
| `view.query` | `asIndent, queryIndent, breakBeforeAs, breakAfterAs` |
| `view.query.singleLine` | `any, whenFitsMargin, maxWords, maxCharacters` |

Breaks: inherit/always/never; spaces: inherit/insert/remove; lists: inherit/on/off with onePerLine/auto. Auto measures a compact list and does not compact lists containing comments. For unbraced procedure parameters, `parameters.breakAfterOpen` means a break before the first parameter. Empty function parentheses have independent `spaceWithinEmpty`; parameter rules do not alter commas inside decimal(p,s) or values. WITH applies only to module options, not CTEs/hints in the body.

All indents use the common enabled/offset/onNewLineOnly/style/transparent type. AS, parentheses and WITH anchor to the module start; parameters/options to the opening parenthesis/WITH. BEGIN/END anchor to AS, inner code to BEGIN, and bodies without BEGIN to AS. `returns.tableIndent` shifts TABLE and the function result definition from the module start, not scalar RETURNS types. For inline functions, `body.breakBefore` controls RETURN, preserving optional AS. CLR EXTERNAL NAME is not treated as a SQL body.

VIEW has independent parentheses/columns, AS, and whole SELECT indentation (from AS). Compaction uses the same independent boolean/threshold fields as cursors: strict thresholds, margin checks including prefix/suffix, and no compaction with comments. Parameter, option, column and expression order/values are preserved.

Verified example:

```json
{"version":2,"rules":{"routine.parameters.stackList":"on","routine.with.breakBefore":"always",
"routine.body.breakBeforeAs":"always","routine.body.breakBefore":"always",
"routine.body.codeIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
"view.columns.stackList":"off","view.query.singleLine.any":true}}
```

Works in Core/CLI and adapters through configuration files, including current-statement scope. Defaults/v1 retain previous output; invalid SQL, multiline literals and gaps adjacent to comments are not rewritten. XML numeric modes are not imported. New switches are available through IDE All settings.

## CREATE TABLE (SC-22)

JSON v2 supports 14 rules for regular table definitions:

| Group | Key suffixes |
| --- | --- |
| `createTable.columns` | `listIndent, braceIndent, breakBeforeOpen, breakAfterOpen, breakBeforeClose, spaceBeforeOpen, spaceWithin, stackList, stackMode` |
| `createTable.storage` | `listIndent, breakBefore, stackList, stackMode` |
| `createTable` | `blankLinesAround` |

Break, space, indent and list types/modes match the earlier sections. The list combines columns, table constraints, indexes and PERIOD FOR SYSTEM_TIME in source order. Outer-parenthesis rules do not affect decimal(p,s), CHECK, composite keys or computed-column expressions.

`storage.breakBefore/listIndent` affect ON, TEXTIMAGE_ON, FILESTREAM_ON and WITH; `storage.stackList/stackMode` affect WITH options, not commas inside PARTITIONS or SYSTEM_VERSIONING. The first WITH item remains next to the opening parenthesis unless the source already wraps it. Parentheses/storage keywords anchor to CREATE TABLE; items to the opening parenthesis/WITH. `blankLinesAround` is boolean: false preserves blank lines, true inserts them between a table and neighboring statements; it does not insert before END/GO or move comments.

Verified configuration:

```json
{"version":2,"rules":{"createTable.columns.breakBeforeOpen":"always","createTable.columns.breakAfterOpen":"always",
"createTable.columns.breakBeforeClose":"always","createTable.columns.stackList":"on",
"createTable.columns.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
"createTable.storage.breakBefore":"always","createTable.storage.stackList":"on"}}
```

Regular/computed columns, constraints, indexes, PERIOD, graph tables with definitions and nested CREATE TABLE statements are supported. CTAS/CLONE and definition-free forms (such as FILETABLE) are not rewritten by this policy. Unknown syntax is safely declined; tokens, order, comments and literals are preserved with parser validation. Defaults/v1 retain previous output; access is through Core/CLI and IDE All settings/JSON, without XML numeric enum import.

## Triggers (SC-23)

JSON v2 configures CREATE/ALTER/CREATE OR ALTER TRIGGER headers and SQL bodies:

| Group | Key suffixes |
| --- | --- |
| `trigger.on` | `keywordIndent, targetIndent, breakBefore, breakAfter` |
| `trigger.with` | `keywordIndent, listIndent, breakBefore, breakAfter, stackList, stackMode` |
| `trigger.events` | `keywordIndent, listIndent, breakBefore, breakAfter, stackList, stackMode` |
| `trigger.body` | `asIndent, keywordIndent, codeIndent, breakBeforeAs, breakAfterAs` |

Types are shared: breaks inherit/always/never, lists inherit/on/off with onePerLine/auto, indents enabled/offset/onNewLineOnly/style/transparent. ON/WITH/FOR/AFTER/INSTEAD OF and AS anchor to the trigger start; targets, options and events to their keyword. ALL SERVER and INSTEAD OF remain phrases. BEGIN/END anchor to AS; the entire inner body to BEGIN, or AS for unblocked bodies. Use Code settings for statement and BEGIN/END breaks inside the body.

DML targets, ON DATABASE, ON ALL SERVER and LOGON are supported. Event order, WITH APPEND, NOT FOR REPLICATION, EXECUTE AS values and body expressions are preserved. Option/event lists are independent, honor global comma settings, and auto does not compact comments. CLR trigger headers can be configured, but EXTERNAL NAME is not treated as a SQL body. Parser-unsupported syntax safely remains unchanged.

Verified example:

```json
{"version":2,"rules":{"trigger.on.breakBefore":"always","trigger.with.breakBefore":"always",
"trigger.events.breakBefore":"always","trigger.events.stackList":"on","trigger.body.breakBeforeAs":"always",
"trigger.body.breakAfterAs":"always","code.breakAfterBegin":"always","code.breakBeforeEnd":"always",
"trigger.body.codeIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false}}}
```

Core/CLI/IDE share the same configuration (through All settings or JSON in IDEs). New rules are opt-in; v1/default retain previous behavior. Tokens/comments are validated, gaps adjacent to comments are skipped, and multiline literals are not rewritten. XML numeric modes are not guessed.

## EXECUTE and labels (SC-24)

JSON v2 adds:

| Group | Keys |
| --- | --- |
| `execute.parameters` | `listIndent, breakBefore, stackList, stackMode` |
| `labels` | `indent, breakAfter, blankLinesAround` |

EXEC/EXECUTE parameters use common indentation anchored to the statement start, inherit/always/never breaks and inherit/on/off lists with onePerLine/auto. Positional/named values, DEFAULT, OUTPUT, return codes, variable procedure names, sp_executesql and dynamic EXEC AT parameters are supported. SQL within strings is never formatted; literal commas are not parameter separators. Parameter-free executable strings are unaffected by this policy.

Label indentation uses enabled/offset/onNewLineOnly/style/transparent: relative anchors to the nearest BEGIN/END, or column zero outside a block; absolute anchors to column zero; transparent is zero indentation. The first label in a file is supported. `breakAfter` controls the gap before the next statement/block. `blankLinesAround` (boolean) adds at least one blank line before the label and after the immediately following statement/block when neighbors exist; false preserves original blank lines. Label names, GOTO and statement order remain unchanged; comments are not moved.

Verified configuration:

```json
{"version":2,"rules":{"execute.parameters.breakBefore":"always","execute.parameters.stackList":"on",
"execute.parameters.listIndent":{"enabled":true,"offset":1,"onNewLineOnly":true,"style":"relative","transparent":false},
"labels.breakAfter":"always","labels.blankLinesAround":true,
"labels.indent":{"enabled":true,"offset":0,"onNewLineOnly":true,"style":"absolute","transparent":false}}}
```

Without new overrides/v1 behavior is unchanged. Common token/reparse/idempotence checks and safe preservation of invalid SQL/multiline literals apply. All 969 applicable profile-ledger paths now have implemented counterparts; 8 OptionHints paths were previously ruled inapplicable by the user. This does not import XML numeric modes. Access is through Core/CLI/IDE JSON; the full UI for new settings has not shipped yet.

## Building a `Doc` from the AST

`SqlDocBuilder` creates a layout document from a parse result. With no additional builders, it preserves the entire source, including comments and `GO` separators:

```csharp
using System;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

var parsed = new ScriptDomSqlParser().Parse("-- note\nSELECT 1;", SqlDialectVersion.Auto);
var document = new SqlDocBuilder().BuildDocument(parsed);
Console.Write(new DocRenderer().Render(document)); // unchanged source
```

When parsing fails, `BuildDocument` also returns the unchanged source. You can register custom `ISqlFragmentDocBuilder` implementations for specific AST node types; earlier builders take precedence. `SqlFragmentWalker` traverses ScriptDom nodes. `ScriptDomSqlFormatter`, rather than a bare `SqlDocBuilder`, installs the built-in structural rules for `SELECT`, `INSERT`, `UPDATE`, `DELETE`, and `MERGE`.

## Limitations

- The CLI writes only stdin or one file to stdout; directories and multiple files require `--write` or `--check`. Configuration discovery applies to files; custom settings flags are not implemented yet.
- Structural Doc formatting covers the documented SELECT, DML and stored-code forms. New Code, module, CREATE TABLE and trigger rules apply only to the documented AST boundaries when explicitly enabled; unsupported forms retain their original layout.
- The renderer accepts a prepared `Doc` tree; it does not parse SQL on its own.
