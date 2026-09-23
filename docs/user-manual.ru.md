# T-SQL Formatter — руководство пользователя

## Текущее состояние

Это ранний прототип. Сейчас доступны программный разбор T-SQL и навигация по токенам через библиотеку `TSqlFormatter.Core`. Форматирование SQL, загрузка конфигурации и команды CLI ещё не реализованы. Готового пакета для установки нет.

## Подготовка

Нужны исходный код проекта и .NET SDK 8.0 или новее. Из корня репозитория выполните:

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
dotnet test TSqlFormatter.sln --no-build --no-restore
```

Чтобы использовать парсер в своём C# проекте, добавьте ссылку на `src/TSqlFormatter.Core/TSqlFormatter.Core.csproj`.

## Разбор T-SQL

```csharp
using System;
using TSqlFormatter.Core.Parsing;

ISqlParser parser = new ScriptDomSqlParser();
var result = parser.Parse("SELECT 1;", SqlDialectVersion.Auto);

if (result.ParseSucceeded)
{
    // result.Root содержит AST ScriptDom, result.Tokens — исходные токены.
}
else
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Message}");
    }
}
```

`result.Source` сохраняет исходный текст. `result.Diagnostics` содержит номер ошибки ScriptDom, сообщение, смещение, строку и столбец. При ошибке `result.Root` может быть частичным, поэтому используйте его только после проверки `ParseSucceeded`.

`SqlDialectVersion` принимает `Auto`, `Sql2016`, `Sql2017`, `Sql2019`, `Sql2022` и `Latest`. Сейчас `Auto` и `Latest` используют парсер ScriptDom `Sql180`; `Auto` не определяет версию сервера. Конструктор `ScriptDomSqlParser` по умолчанию включает режим quoted identifiers; для другого начального режима передайте `initialQuotedIdentifiers: false`.

## Навигация по токенам и фрагментам

После успешного разбора создайте `SqlTokenNavigator` для того же результата:

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

`GetToken(index)` возвращает токен ровно по индексу в `result.Tokens`. `GetPreviousMeaningfulToken(index)` и `GetNextMeaningfulToken(index)` ищут строго до и после указанного токена, пропуская пробелы, однострочные и многострочные комментарии, а также конечный маркер; если подходящего токена нет, они возвращают `null`. При индексе вне диапазона эти методы выбрасывают `ArgumentOutOfRangeException`.

`GetFragmentTokens(fragment)` возвращает все токены от `FirstTokenIndex` до `LastTokenIndex` включительно, сохраняя комментарии и пробелы внутри фрагмента. `GetTextSpan(fragment)` возвращает диапазон исходного текста: `StartOffset`, `Length` и исключающую границу `EndOffset`. Смещения отсчитываются от нуля в символах .NET. Для `null`-фрагмента выбрасывается `ArgumentNullException`, для фрагмента с недопустимыми границами — `ArgumentException`. При ошибке разбора навигация по доступным токенам работает, но AST-фрагменты могут быть частичными.

## Ограничения

- CLI пока является заготовкой: он не форматирует SQL и не предоставляет пользовательских команд.
- Парсер не меняет SQL и не создаёт отформатированный текст.
- Преобразование смещений в строки и столбцы (`LineMap`) пока не реализовано.
