# T-SQL Formatter — user manual

## Current status

This is an early prototype. Programmatic T-SQL parsing is available through `TSqlFormatter.Core`. SQL formatting, configuration loading, and CLI commands are not implemented yet. There is no installable package.

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

## Limitations

- The CLI is a placeholder: it does not format SQL or provide user commands yet.
- The parser does not modify SQL or produce formatted text.
- `LineMap` and token navigation helpers are planned for later stages.
