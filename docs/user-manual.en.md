# T-SQL Formatter — user manual

## Current status

This is an early prototype. Programmatic T-SQL parsing, token navigation, offset-to-line mapping, and layout document construction and rendering are available through `TSqlFormatter.Core`. Automatic SQL formatting, configuration loading, and CLI commands are not implemented yet. There is no installable package.

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

By default, `DocRenderOptions` uses a width of 100 UTF-16 code units, 4 spaces per indentation level, LF, and no final newline. You can set `maxLineWidth`, `indentWidth`, `lineEnding` (`Lf`, `CrLf`, `Cr`), `finalNewline`, and `useTabs`. The selected EOL applies to document breaks; line endings inside `TextDoc` are preserved. An empty document stays empty even with `finalNewline: true`.

## Limitations

- The CLI is a placeholder: it does not format SQL or provide user commands yet.
- The parser does not modify SQL or produce formatted text.
- The renderer accepts a prepared `Doc` tree, but it does not build one from a T-SQL AST or format arbitrary SQL yet.
