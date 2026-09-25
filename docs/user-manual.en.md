# T-SQL Formatter — user manual

## Current status

This is an early prototype. T-SQL parsing, token navigation, comment classification, offset-to-line mapping, layout document construction and rendering, keyword casing, and formatting for supported `SELECT`, `INSERT`, `UPDATE`, `DELETE`, and `MERGE` forms are available through `TSqlFormatter.Core`, including CTEs, subqueries, `CASE`, window functions, `FROM`, `JOIN`, `APPLY`, and comments in supported positions. JSON settings and named profiles are available through `TSqlFormatter.Configuration`. The CLI formats SQL from stdin or one file to stdout; automatic config-file discovery is not implemented yet. There is no installable package.

## Setup

You need the project source and .NET SDK 8.0 or newer. Run these commands from the repository root:

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
dotnet test TSqlFormatter.sln --no-build --no-restore
```

To use the parser in your C# project, add a reference to `src/TSqlFormatter.Core/TSqlFormatter.Core.csproj`.

## CLI: stdin or one file → stdout

After building, pass SQL through stdin from the repository root:

```powershell
'select Id from T' | dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore
```

Formatted SQL goes to stdout; `-` can be passed instead of no argument. `--help` shows brief usage. Diagnostics go to stderr. Exit code `0` means formatting succeeded; `2` means a SQL parse, input read, or argument error; code `1` is not used yet. No SQL is printed on a parse error. The CLI uses built-in `Default` options, does not search for `.tsqlformatter.json`, and does not yet accept `--write`, `--check`, or settings flags.

To read one `query.sql` file from the current directory:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql
```

The CLI reads the file as UTF-8 (including BOM support), writes formatted SQL to stdout, and does not modify the source file. A missing or unreadable file returns code `2`, writes an error to stderr, and leaves stdout empty. Multiple files in one invocation are not supported yet.

## JSON configuration

To read and write settings, reference `src/TSqlFormatter.Configuration/TSqlFormatter.Configuration.csproj`. Version 1 of `.tsqlformatter.json` supports the current options-model sections:

```json
{
  "version": 1,
  "general": { "maxLineLength": 100, "lineEnding": "lf", "finalNewLine": false },
  "indent": { "style": "spaces", "size": 4 },
  "keywords": { "case": "upper" },
  "select": { "columns": "auto" },
  "clauses": { "groupByLayout": "auto", "orderByLayout": "auto" }
}
```

`lineEnding` accepts `lf`, `crlf`, or `cr`; `indent.style` accepts `spaces` or `tabs`; `keywords.case` accepts `upper`, `lower`, or `preserve`; layouts accept `auto` or `onePerLine`. `general.maxLineLength` must be an integer of at least 1, and `indent.size` an integer of at least 0. Missing sections and properties use built-in defaults. Serialization writes all supported properties and a final LF.

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

This explicitly reads a file in your application: neither the CLI nor the formatter discovers config files automatically yet. The plan's example also shows future fields (`joins`, `expressions`, `aliases`, and others); the current serializer rejects them as unknown.

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

`FormattingOptionsOverrides` parameters map to the supported JSON fields: `maxLineLength`, `lineEnding`, `finalNewLine`, `indentSize`, `useTabs`, `keywordCase`, `selectColumns`, `groupByLayout`, and `orderByLayout`. This is an application API; the CLI does not yet accept option flags or load files automatically.

### Named profiles

`FormattingProfileCatalog` provides `Default` (standard values), `Compact` (line width 120), and `Expanded` (line width 80; `SELECT` columns and `GROUP BY`/`ORDER BY` items one per line). IDs are matched case-insensitively. Select a profile with `profileId`:

```csharp
var projectProfile = new FormattingProfile(
    "project", "Project", FormattingOptions.Default.With(indent: new IndentOptions(2)));
var catalog = new FormattingProfileCatalog(new[] { projectProfile });
var configured = new SqlFormatterConfigurationResolver(catalog).Resolve(
    json, profileId: "project");
```

An application can supply custom user or project profiles when creating the catalog. Duplicate IDs, including collisions with built-ins, are rejected. An unknown `profileId` returns `TSF2000` with `Options == null`. File fields overlay the profile rather than resetting it to `Default`. Persisting profiles in JSON, CLI profile selection, and a profile UI are not implemented yet.

To check comment preservation separately, run the golden suite:

```powershell
dotnet test tests/TSqlFormatter.GoldenTests/TSqlFormatter.GoldenTests.csproj --no-restore
```

The suite contains 50 fixed expected-SQL cases with leading, inline, block, and standalone comments. It checks that each comment appears once and that formatting again does not change the result. These checks are also included in `dotnet test TSqlFormatter.sln`.

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

`SqlDialectVersion` accepts `Auto`, `Sql2016`, `Sql2017`, `Sql2019`, `Sql2022`, and `Latest`. Currently, `Auto` and `Latest` use ScriptDom parser `Sql180`; `Auto` does not detect a server version. `ScriptDomSqlParser` enables quoted identifiers by default; pass `initialQuotedIdentifiers: false` to change the initial setting.

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

Each `SqlCommentTrivia` carries an exact `Span`, original `Text`, `Kind` (`Line`/`Block`), `TokenIndex`, and `Placement`: `Trailing` follows a SQL token on the same line; `Leading` is adjacent to the next token (including a contiguous chain of comments); `Standalone` has no such attachment, for example when separated by a blank line. `AnchorTokenIndex` refers to the attached SQL token in `parsed.Tokens`; it is `null` for `Standalone`. The scanner also works with available tokens after a parse error. The formatter uses this classification for line and block comments after commas in `SELECT` and before supported clauses; other placements do not yet have dedicated formatting rules.

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

`FormatRequest` defaults to whole-document scope (`Document`), the `Auto` dialect, and strict parse-failure behavior (`Strict`). `Selection` requires a `SqlTextSpan`; `Selection` and `Statement` currently return unchanged source with a `TSF3000` warning. On a parse error, source remains unchanged, `ParseSucceeded` is `false`, and a `TSF1000` diagnostic is reported for all of `Strict`, `Safe`, and `TokenFallback`.

`FormattingOptions` groups settings into `General` (width 100, LF, no final newline), `Indent` (4 spaces, no tabs), `Keywords` (`Upper`), `Select` (`Auto`), and `Clauses` (`Auto` for `GROUP BY` and `ORDER BY`). Indentation and EOL apply to supported `SELECT`, `INSERT`, `UPDATE`, `DELETE`, and `MERGE` output; width and list layouts affect supported `SELECT` lists. Otherwise, only keyword casing currently changes. `FormatResult` carries final text, `TextEdit` changes, diagnostics, change status, and parse success.

`FormattingOptions.Default` supplies the same values as `new FormattingOptions()`. `With` creates a new option set by replacing only the supplied sections, leaving the original unchanged:

```csharp
var options = FormattingOptions.Default.With(
    general: new GeneralOptions(maxLineWidth: 120),
    keywords: new KeywordOptions(KeywordCase.Lower));
```

The public sections currently include only options backed by implemented behavior. Planned `JOIN`, `CASE`, comment, and other settings are not part of the API yet.

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

Predicates such as `IS NULL` and `LIKE` are not supported yet. They retain their original layout, although keyword casing may change. Internal whitespace in expressions on either side of a comparison is preserved.

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

One or more simple CTEs before the main `SELECT` are supported. Each CTE's nested query is indented; an optional column-name list is preserved:

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

CTEs with `XMLNAMESPACES`, nested `WITH`, or an unsupported query expression retain their original layout for now. CTE formatting does not yet cover `INSERT`/`UPDATE`/`DELETE`.

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

Condition and result expression text within each branch is retained. If comments or another unsupported construct appear between CASE parts, the query's original layout is retained; keyword casing may still change.

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

If a comment appears between a query and the operator, the entire expression keeps its original layout; only keyword casing may change. Combining such an expression with a CTE or `OFFSET/FETCH` does not yet receive structural formatting.

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

The function arguments retain their original text. Window frames (`ROWS`/`RANGE`), named windows, and window functions embedded in more complex scalar expressions do not yet receive structural formatting. In these cases, original layout is retained, though recognized keywords may change case.

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

Multiple such comments in one list are supported. Spacing before `--` is normalized to one space and line endings follow `GeneralOptions.LineEnding`; the comment text itself is unchanged. Comments before a column do not yet have a dedicated placement rule.

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

`INSERT [INTO] table [(columns)] VALUES` with one or more rows and `INSERT [INTO] table [(columns)] SELECT` are supported. Target columns are normalized with comma-space separators, and each `VALUES` row gets its own line:

```sql
INSERT INTO dbo.T (Id, Name)
VALUES
    (1, 'a'),
    (2, 'b');
```

In `INSERT ... SELECT`, the nested query follows the supported `SELECT` formatting rules. Column and value order is retained. `INSERT ... EXEC`, `DEFAULT VALUES`, a CTE before `INSERT`, and comments between value rows retain their original layout for now; recognized keywords may still change case.

## UPDATE

A basic `UPDATE` with ordinary `SET` assignments and optional `FROM` and `WHERE` is formatted clause by clause. Each assignment gets its own line; `FROM` supports the same simple tables and joins as `SELECT`:

```sql
UPDATE t
SET
    Name = 'x',
    Count = 2
FROM dbo.T t
WHERE
    t.Id = 1;
```

Assignment order is retained. Compound assignments such as `+=`, `TOP`, and a CTE before `UPDATE` do not yet receive structural formatting. Unsupported constructs retain their original layout, though recognized keywords may change case.

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

`DELETE TOP` and a CTE before `DELETE` retain their original layout for now; recognized keywords may change case. Other `FROM` and `WHERE` limitations match those described for supported `SELECT` and `UPDATE`.

## OUTPUT

In supported `INSERT`, `UPDATE`, and `DELETE`, the `OUTPUT` clause starts on its own line. Its projection list uses comma-space separators; `OUTPUT ... INTO table [(columns)]` is also supported:

```sql
DELETE FROM T
OUTPUT deleted.Id INTO dbo.Audit (Id)
WHERE
    Id = 1;
```

Original expressions and their order are retained. A comment between `OUTPUT` items or an unsupported `INTO` target leaves the entire statement's layout unchanged. Supported `MERGE` statements can also use `OUTPUT` or `OUTPUT ... INTO`.

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

`WHEN NOT MATCHED BY SOURCE THEN DELETE` is also supported. Additional `AND` branch conditions, compound assignments, a CTE before `MERGE`, and unsupported sources retain their original layout; recognized keywords may still change case.

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

- The CLI accepts stdin or one file; `--write`, `--check`, and configuration discovery are not implemented yet.
- Structural formatting covers only the `SELECT`, `INSERT`, `UPDATE`, `DELETE`, and `MERGE` forms described above; other constructs retain their original layout.
- The renderer accepts a prepared `Doc` tree; it does not parse SQL on its own.
