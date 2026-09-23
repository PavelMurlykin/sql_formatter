# T-SQL Formatter — user manual

## Current status

This is an early prototype. T-SQL parsing, token navigation, offset-to-line mapping, layout document construction and rendering, and programmatic keyword casing are available through `TSqlFormatter.Core`. Structural SQL formatting, configuration loading, and CLI commands are not implemented yet. There is no installable package.

## Setup

You need the project source and .NET SDK 8.0 or newer. Run these commands from the repository root:

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
dotnet test TSqlFormatter.sln --no-build --no-restore
```

To use the parser in your C# project, add a reference to `src/TSqlFormatter.Core/TSqlFormatter.Core.csproj`.

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

`ScriptDomSqlFormatter` implements `ISqlFormatter.Format(source, options, request, cancellationToken)`. It currently changes only tokens ScriptDom recognizes as keywords; whitespace, strings, comments, and identifiers are preserved. The default is `Upper`; `Lower` and `Preserve` are also available:

```csharp
using TSqlFormatter.Core.Formatting;

ISqlFormatter formatter = new ScriptDomSqlFormatter();
var result = formatter.Format("select 'from' from dbo.Items",
    new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Upper)),
    new FormatRequest());
System.Console.WriteLine(result.Text); // SELECT 'from' FROM dbo.Items
```

`FormatRequest` defaults to whole-document scope (`Document`), the `Auto` dialect, and strict parse-failure behavior (`Strict`). `Selection` requires a `SqlTextSpan`; `Selection` and `Statement` currently return unchanged source with a `TSF3000` warning. On a parse error, source remains unchanged, `ParseSucceeded` is `false`, and a `TSF1000` diagnostic is reported for all of `Strict`, `Safe`, and `TokenFallback`.

`FormattingOptions` groups settings into `General` (width 100, LF, no final newline), `Indent` (4 spaces, no tabs), and `Keywords` (`Upper`). The formatter currently uses only `Keywords`. `FormatResult` carries final text, `TextEdit` changes, diagnostics, change status, and parse success.

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

When parsing fails, `BuildDocument` also returns the unchanged source. You can register custom `ISqlFragmentDocBuilder` implementations for specific AST node types; earlier builders take precedence. `SqlFragmentWalker` traverses ScriptDom nodes. These extension points do not yet provide built-in structural formatting rules. Use `ScriptDomSqlFormatter` for keyword casing.

## Limitations

- The CLI is a placeholder: it does not format SQL or provide user commands yet.
- The formatter does not yet change SQL structure, whitespace, or line breaks.
- The renderer accepts a prepared `Doc` tree; built-in AST handlers currently preserve source text.
