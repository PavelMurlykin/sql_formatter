# T-SQL Formatter — руководство пользователя

## Текущее состояние

Это ранний прототип. Через `TSqlFormatter.Core` доступны разбор T-SQL, навигация по токенам, перевод между смещениями и позициями строк, построение и рендеринг layout-документа, изменение регистра ключевых слов и базовое форматирование `SELECT`, `FROM`, `JOIN` и `APPLY`. Загрузка конфигурации и команды CLI ещё не реализованы. Готового пакета для установки нет.

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

`DocRenderOptions` по умолчанию использует ширину 100 кодовых единиц UTF-16, 4 пробела на уровень, LF, без конечного перевода строки. Можно задать `maxLineWidth`, `indentWidth`, `lineEnding` (`Lf`, `CrLf`, `Cr`), `finalNewline` и `useTabs`. Заданный EOL применяется к переносам узлов; переводы строк внутри `TextDoc` сохраняются как есть. Рендерер удаляет созданные им завершающие пробелы, но не изменяет буквальный текст `TextDoc`. Пустой документ остаётся пустым даже при `finalNewline: true`.

## Измерение производительности рендерера

Из корня репозитория после `dotnet restore TSqlFormatter.sln` запустите BenchmarkDotNet в Release:

```powershell
dotnet run --project benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -c Release --no-restore -- --filter '*DocRendererBenchmarks*'
```

Сценарии `SmallFlat`, `MediumWrapped` и `LargeWrapped` измеряют только `DocRenderer.Render` на заранее построенных документах с 5, 50 и 500 колонками. Отчёт включает время и выделения памяти; он не измеряет разбор SQL, создание дерева или полное форматирование. Для короткой проверки запуска можно добавить `--job Dry`, но его одно измерение не следует использовать для сравнения производительности. Первый запуск может потребовать доступ к NuGet для дочернего проекта BenchmarkDotNet.

## Регистр ключевых слов

`ScriptDomSqlFormatter` реализует `ISqlFormatter.Format(source, options, request, cancellationToken)`. Он изменяет регистр токенов, распознанных ScriptDom как ключевые слова, и операторов поддержанных `JOIN`/`APPLY`, подтверждённых AST; строки, комментарии и идентификаторы сохраняются. По умолчанию выбирается `Upper`; доступны `Lower` и `Preserve`:

```csharp
using TSqlFormatter.Core.Formatting;

ISqlFormatter formatter = new ScriptDomSqlFormatter();
var result = formatter.Format("select 'from' from dbo.Items",
    new FormattingOptions(keywords: new KeywordOptions(KeywordCase.Upper)),
    new FormatRequest());
System.Console.WriteLine(result.Text); // SELECT 'from' FROM dbo.Items
```

`FormatRequest` по умолчанию запрашивает весь документ (`Document`), диалект `Auto` и строгое поведение при ошибке разбора (`Strict`). Область `Selection` требует `SqlTextSpan`; `Selection` и `Statement` пока возвращают исходный текст с предупреждением `TSF3000`. При ошибке разбора исходный текст остаётся неизменным, `ParseSucceeded` равен `false`, выдаётся диагностика `TSF1000` независимо от выбранного режима `Strict`, `Safe` или `TokenFallback`.

`FormattingOptions` разделяет параметры на `General` (ширина 100, LF, без конечного перевода строки), `Indent` (4 пробела, без табуляции), `Keywords` (`Upper`), `Select` (`Auto`) и `Clauses` (`Auto` для `GROUP BY` и `ORDER BY`). Для поддержанного `SELECT` применяются параметры ширины, отступа, EOL и раскладки списков; в остальных случаях пока меняется только регистр ключевых слов. `FormatResult` содержит итоговый текст, правки `TextEdit`, диагностики, признаки изменения и успешности разбора.

## Базовый SELECT

Для простого `SELECT` с литералами или колонками форматтер упорядочивает пробелы между элементами, сохраняет имена и алиасы, а `FROM` переносит на отдельную строку:

```csharp
var result = new ScriptDomSqlFormatter().Format(
    "select u.Id as UserId,u.Name from dbo.Users u;",
    new FormattingOptions(), new FormatRequest());
System.Console.WriteLine(result.Text);
// SELECT u.Id AS UserId, u.Name
// FROM dbo.Users u;
```

Пока структурно поддержаны простые списки колонок, одна таблица в `FROM`, базовый `WHERE`, `GROUP BY`, `HAVING` и `ORDER BY`. При наличии комментариев внутри конструкции либо пока неподдержанного предложения исходное расположение сохраняется, но регистр ключевых слов всё равно может измениться. Завершающая точка с запятой сохраняется.

### Условия WHERE

Простые сравнения (`=`, `<>`, `!=`, `<`, `>`, `<=`, `>=`, `!<`, `!>`) форматируются с пробелами вокруг оператора. Условия `AND` и `OR` начинаются с новой строки под `WHERE`:

```sql
SELECT Id
FROM Items
WHERE
    Id >= @Min
    AND State = 'open';
```

Пока не поддержаны, например, `IS NULL` и `LIKE`. Для них исходная раскладка сохраняется и может измениться только регистр ключевых слов. Внутренние пробелы в выражениях по сторонам оператора сравнения сохраняются.

### Вложенные скобки

Логические группы из поддержанных сравнений и `AND`/`OR` можно вкладывать друг в друга. Скобки сохраняются, содержимое получает дополнительный отступ:

```sql
WHERE
    (
        A = 1
        OR B = 2
    )
    AND C = 3
```

В производной таблице также поддержано выражение запроса, дополнительно заключённое в скобки, например `FROM ((SELECT Id FROM T)) AS d`. Каждый уровень скобок выводится отдельным вложенным блоком. Другие сложные выражения запросов пока сохраняют исходную раскладку.

### GROUP BY, HAVING и ORDER BY

Для базового `GROUP BY` и `ORDER BY` элементы списка разделяются запятыми после элементов. `ASC` и `DESC` сохраняются. `HAVING` поддерживает те же простые сравнения и `AND`/`OR`, что и `WHERE`:

```sql
SELECT Category, count(*) AS Total
FROM Sales
GROUP BY Category
HAVING
    count(*) > 1
ORDER BY Total DESC;
```

Параметры `QueryClauseOptions.GroupByLayout` и `OrderByLayout` принимают `ClauseItemLayout.Auto` (по умолчанию) или `OnePerLine`. `Auto` оставляет список в одной строке, если он помещается в `GeneralOptions.MaxLineWidth`, иначе переносит по элементу на строку. `OnePerLine` переносит всегда. Например: `new FormattingOptions(clauses: new QueryClauseOptions(ClauseItemLayout.OnePerLine, ClauseItemLayout.OnePerLine))`. Группировки `ROLLUP`/`CUBE`, `ORDER BY ALL` и `OFFSET/FETCH` пока не получают структурное форматирование.

### Источник данных FROM

Поддержаны имена таблиц, включая `schema.table` и заключённые в квадратные скобки части, а также алиасы с `AS` или без него. Базовая производная таблица из `SELECT` оформляется как вложенный запрос с отступом:

```sql
SELECT d.Id
FROM (
    SELECT Id
    FROM dbo.Items
) AS d;
```

Список переименования колонок производной таблицы (`AS d(Id)`) пока не форматируется структурно: сохраняется исходное расположение, применяется лишь регистр ключевых слов.

### JOIN и APPLY

Поддержаны `INNER JOIN` (и краткое `JOIN`), `LEFT [OUTER] JOIN`, `RIGHT [OUTER] JOIN`, `FULL [OUTER] JOIN`, `CROSS JOIN`, `CROSS APPLY` и `OUTER APPLY`. Каждое соединение цепочки начинается на новой строке. Условие `ON` располагается отдельной строкой с отступом; базовые сравнения и логические группы форматируются, более сложные выражения сохраняются как есть:

```sql
SELECT a.Id
FROM dbo.A a
INNER JOIN dbo.B b
    ON a.Id = b.Id
LEFT JOIN dbo.C c
    ON b.Id = c.Id;
```

Для `APPLY` справа можно использовать простую производную таблицу. Подсказки соединения вроде `HASH JOIN` и другие пока неподдержанные формы сохраняют исходную раскладку. При этом регистр токенов, которые ScriptDom распознал как ключевые слова, по-прежнему может измениться.

### Раскладка колонок

`SelectOptions.ColumnLayout` принимает `Auto` (по умолчанию) или `OnePerLine`. В режиме `Auto` весь список остаётся в одной строке, если помещается в `GeneralOptions.MaxLineWidth`; иначе каждая колонка переносится на строку с отступом. `OnePerLine` всегда переносит каждую колонку. Запятая остаётся после колонки, кроме последней:

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

## Построение `Doc` из AST

`SqlDocBuilder` создаёт layout-документ из результата парсинга. Без дополнительных обработчиков он сохраняет весь исходный текст, включая комментарии и разделители `GO`:

```csharp
using System;
using TSqlFormatter.Core.Formatting.Builders;
using TSqlFormatter.Core.Layout;
using TSqlFormatter.Core.Parsing;

var parsed = new ScriptDomSqlParser().Parse("-- note\nSELECT 1;", SqlDialectVersion.Auto);
var document = new SqlDocBuilder().BuildDocument(parsed);
Console.Write(new DocRenderer().Render(document)); // исходный текст без изменений
```

При неудачном разборе `BuildDocument` также возвращает неизменённый исходный текст. Для отдельных видов AST-узлов можно зарегистрировать собственные `ISqlFragmentDocBuilder`; более ранний обработчик имеет приоритет. `SqlFragmentWalker` позволяет обойти узлы ScriptDom. Эти точки расширения пока не добавляют готовых правил структурного форматирования. Для изменения регистра используйте `ScriptDomSqlFormatter`.

## Ограничения

- CLI пока является заготовкой: он не форматирует SQL и не предоставляет пользовательских команд.
- Структурное форматирование пока ограничено простым `SELECT`; сложные запросы сохраняют исходное расположение.
- Рендерер принимает готовое дерево `Doc`; встроенные обработчики AST пока сохраняют исходный текст.
