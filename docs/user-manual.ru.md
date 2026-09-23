# T-SQL Formatter — руководство пользователя

## Текущее состояние

Это ранний прототип. Сейчас доступны программный разбор T-SQL, навигация по токенам, перевод между смещениями и позициями строк, а также построение и рендеринг layout-документа через библиотеку `TSqlFormatter.Core`. Автоматическое форматирование SQL, загрузка конфигурации и команды CLI ещё не реализованы. Готового пакета для установки нет.

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

## Позиции в исходном тексте

Каждый `SqlParseResult` содержит `LineMap`, построенную по неизменённому исходному тексту:

```csharp
using System;
using TSqlFormatter.Core.Parsing;

var result = new ScriptDomSqlParser().Parse("SELECT 1;\r\nSELECT 2;", SqlDialectVersion.Auto);
var offset = result.Source.IndexOf("SELECT 2;", StringComparison.Ordinal);
var position = result.LineMap.GetLinePosition(offset);
Console.WriteLine($"{position.Line}:{position.Column}"); // 2:1
Console.WriteLine(result.LineMap.GetOffset(position)); // исходное смещение
```

`GetLinePosition(offset)` переводит смещение в `SqlLinePosition` со свойствами `Line` и `Column`; `GetOffset(line, column)` или `GetOffset(position)` выполняет обратный перевод. `LineCount` показывает число строк. Смещения начинаются с 0 и включают позицию после последнего символа; строки и столбцы начинаются с 1. Столбцы считаются в кодовых единицах UTF-16, а не в видимых символах. Поддерживаются переводы строк LF, CRLF и одиночный CR. Оба символа CRLF относятся к предыдущей строке, следующая начинается после LF; это позволяет точно восстановить любое смещение, включая позицию внутри CRLF. Пустой текст содержит одну строку с позицией `1:1`. Недопустимые смещения и позиции вызывают `ArgumentOutOfRangeException`.

## Модель layout-документа для разработчиков

В `TSqlFormatter.Core.Layout` можно составить дерево из `TextDoc`, `ConcatDoc`, `SoftLineDoc.Instance`, `HardLineDoc.Instance`, `IndentDoc`, `GroupDoc` и `IfBreakDoc`. Например:

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

`SoftLineDoc` означает пробел при размещении в одной строке или перенос при разбиении; `HardLineDoc` — обязательный перенос. `GroupDoc` пытается оставить содержимое в одной строке, если оно помещается. `IndentDoc` задаёт неотрицательное число уровней отступа, а `IfBreakDoc(broken, flat)` выбирает вариант согласно режиму группы.

`DocRenderOptions` по умолчанию использует ширину 100 кодовых единиц UTF-16, 4 пробела на уровень, LF, без конечного перевода строки. Можно задать `maxLineWidth`, `indentWidth`, `lineEnding` (`Lf`, `CrLf`, `Cr`), `finalNewline` и `useTabs`. Заданный EOL применяется к переносам узлов; переводы строк внутри `TextDoc` сохраняются как есть. Пустой документ остаётся пустым даже при `finalNewline: true`.

## Ограничения

- CLI пока является заготовкой: он не форматирует SQL и не предоставляет пользовательских команд.
- Парсер не меняет SQL и не создаёт отформатированный текст.
- Рендерер принимает готовое дерево `Doc`, но пока не строит его из AST T-SQL и не форматирует произвольный SQL.
