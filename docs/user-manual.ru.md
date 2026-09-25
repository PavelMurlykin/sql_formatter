# T-SQL Formatter — руководство пользователя

## Текущее состояние

Это ранний прототип. Через `TSqlFormatter.Core` доступны разбор T-SQL, навигация по токенам, классификация комментариев, перевод между смещениями и позициями строк, построение и рендеринг layout-документа, изменение регистра ключевых слов и форматирование поддержанных форм `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE`, включая CTE, подзапросы, `CASE`, оконные функции, `FROM`, `JOIN` и `APPLY`, а также комментарии в поддержанных позициях. Через `TSqlFormatter.Configuration` доступны настройки JSON и именованные профили. CLI форматирует SQL из stdin или одного файла в stdout, поддерживает `--write`, `--check` и автоматический поиск конфигурации для файла. Готового пакета для установки нет.

## Подготовка

Нужны исходный код проекта и .NET SDK 8.0 или новее. Из корня репозитория выполните:

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
dotnet test TSqlFormatter.sln --no-build --no-restore
```

Чтобы использовать парсер в своём C# проекте, добавьте ссылку на `src/TSqlFormatter.Core/TSqlFormatter.Core.csproj`.

## CLI: stdin или один файл → stdout

Из корня репозитория после сборки передайте SQL через stdin:

```powershell
'select Id from T' | dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore
```

Результат выводится в stdout; вместо отсутствующего аргумента можно передать `-`. `--help` показывает краткую справку. Диагностики идут в stderr. Код завершения `0` означает успешное форматирование, `2` — ошибку разбора SQL, конфигурации, ввода-вывода или аргументов. Код `1` используется только в режиме `--check`, когда форматирование требуется. При ошибке разбора SQL не выводится. Для stdin CLI использует `Default`; для файла ищет `.tsqlformatter.json`. Флаги настроек пока не поддерживаются.

Чтобы прочитать один файл `query.sql` из текущего каталога:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql
```

CLI читает файл как UTF-8 (с поддержкой BOM), выводит форматированный SQL в stdout и не изменяет исходный файл. При отсутствии файла или ошибке чтения возвращается код `2`, сообщение пишется в stderr, stdout остаётся пустым. Два файла за один вызов пока не поддерживаются.

Чтобы записать результат в тот же файл, добавьте `--write` после пути:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql --write
```

В этом режиме stdout пуст, наличие UTF-8 BOM сохраняется, а уже отформатированный файл не перезаписывается. При невалидном SQL или ошибке записи исходный файл остаётся без изменений; возвращается код `2` и диагностика в stderr. Для stdin (`-`) режим `--write` недоступен.

Для проверки без изменений файла используйте `--check`:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql --check
$LASTEXITCODE
```

Код `0` означает, что файл уже отформатирован; `1` — что форматирование нужно; `2` — ошибку SQL, конфигурации, чтения или аргументов. `--check` не пишет в файл и оставляет stdout пустым. Для stdin (`-`) он пока недоступен.

### Поиск конфигурации для файла

Для `query.sql`, в том числе с `--write` или `--check`, CLI сначала проверяет `.tsqlformatter.json` в каталоге файла, затем в родительских каталогах. Используется ближайший файл. Каталог с папкой или файлом `.git` проверяется последним; выше него поиск не идёт. Если такого маркера нет, поиск продолжается до корня файловой системы. Путь рассматривается как абсолютный после разрешения относительно текущего каталога; символические ссылки при обходе родителей не раскрываются. Для stdin поиска нет.

Файл читается как UTF-8, проверяется по схеме версии 1, и найденные настройки накладываются на `Default`. При невалидной конфигурации CLI выводит `TSF2000` и путь к файлу в stderr, возвращает `2`, не печатает SQL и не выполняет `--write`. При ошибке чтения возвращается `TSF9000`. Если конфигурации нет, используются встроенные значения.

### Крупные входные данные

CLI читает SQL и конфигурацию с ограничением: не более 64 МиБ для каждого файла и не более 16 Ми символов UTF-16 после декодирования (для stdin действует ограничение по символам). При превышении возвращается код `2` с `TSF9001` в stderr: SQL не выводится, `--write` не выполняется. Ошибка UTF-8 даёт `TSF9000`. Чтение и запись файлов не создают дополнительную полную копию содержимого в массиве байтов; сам парсер по-прежнему работает с текстом целиком, поэтому произвольно большие файлы пока не поддерживаются.

## Конфигурация JSON

Для чтения и записи настроек добавьте ссылку на `src/TSqlFormatter.Configuration/TSqlFormatter.Configuration.csproj`. Поддерживается файл `.tsqlformatter.json` версии 1 с текущими разделами модели настроек:

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

`lineEnding` принимает `lf`, `crlf` или `cr`; `indent.style` — `spaces` или `tabs`; `keywords.case` — `upper`, `lower` или `preserve`; раскладки — `auto` или `onePerLine`. `general.maxLineLength` должен быть целым числом не меньше 1, `indent.size` — целым числом не меньше 0. Отсутствующие разделы и поля получают встроенные значения по умолчанию. Сериализатор записывает все поддержанные поля и завершающий LF.

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

Это явное чтение файла приложением. CLI ищет конфигурацию автоматически только при работе с именованным SQL-файлом; `ScriptDomSqlFormatter` сам этого не делает. Файл плана показывает также будущие поля (`joins`, `expressions`, `aliases` и другие); текущий сериализатор отклоняет их как неизвестные.

Для обработки ошибок без исключения используйте `Parse`:

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

Неизвестные разделы и поля, неподдерживаемая версия, дублирующиеся ключи, неправильные типы и недопустимые значения дают диагностику `TSF2000` уровня `Error`. При любой ошибке `Options` равен `null`; независимые ошибки полей собираются вместе. `Deserialize` для невалидной конфигурации выбрасывает `JsonSerializationException`. Например, показанный в плане `lineEnding: "auto"` пока не поддерживается; его нужно заменить на `lf`, `crlf` или `cr`.

### Приоритет настроек

`SqlFormatterConfigurationResolver` объединяет выбранный профиль (по умолчанию `Default`), содержимое явно переданного файла и явно заданные параметры в таком порядке. Последний слой переопределяет только указанные поля; остальные настройки файла и профиля сохраняются. Отсутствующий файл передавайте как `null`. При ошибке JSON результат содержит диагностики и не содержит частично применённых настроек.

```csharp
var json = File.ReadAllText(".tsqlformatter.json");
var resolved = new SqlFormatterConfigurationResolver().Resolve(
    json,
    new FormattingOptionsOverrides(maxLineLength: 120, keywordCase: KeywordCase.Lower));
if (!resolved.Succeeded)
    throw new System.InvalidOperationException(resolved.Diagnostics[0].Message);
var effectiveOptions = resolved.Options!;
```

Параметры `FormattingOptionsOverrides` соответствуют поддержанным полям JSON: `maxLineLength`, `lineEnding`, `finalNewLine`, `indentSize`, `useTabs`, `keywordCase`, `selectColumns`, `groupByLayout` и `orderByLayout`. Это API для приложений; CLI загружает найденный файл автоматически, но пока не принимает флаги настроек.

### Именованные профили

`FormattingProfileCatalog` содержит `Default` (обычные значения), `Compact` (ширина строки 120) и `Expanded` (ширина 80, колонки `SELECT` и элементы `GROUP BY`/`ORDER BY` по одному на строку). Идентификаторы сравниваются без учёта регистра. Выберите профиль через `profileId`:

```csharp
var projectProfile = new FormattingProfile(
    "project", "Project", FormattingOptions.Default.With(indent: new IndentOptions(2)));
var catalog = new FormattingProfileCatalog(new[] { projectProfile });
var configured = new SqlFormatterConfigurationResolver(catalog).Resolve(
    json, profileId: "project");
```

Приложение может передать свои пользовательские или проектные профили при создании каталога. Повторяющиеся идентификаторы, включая совпадение со встроенными, отклоняются. Неизвестный `profileId` даёт `TSF2000` и `Options == null`. Поля файла накладываются на профиль, а не сбрасывают его к `Default`. Хранение профилей в JSON, переключение через CLI и интерфейс профилей пока не реализованы.

Для проверки сохранности комментариев отдельно запустите golden-набор:

```powershell
dotnet test tests/TSqlFormatter.GoldenTests/TSqlFormatter.GoldenTests.csproj --no-restore
```

Набор содержит 50 фиксированных случаев с ожидаемым SQL: ведущие, строчные, блочные и отдельно стоящие комментарии. Он проверяет, что каждый комментарий остаётся один раз, а повторное форматирование не меняет результат. Эта проверка также входит в `dotnet test TSqlFormatter.sln`.

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

## Классификация комментариев

`SqlTriviaScanner` анализирует токены результата разбора, не меняя исходный SQL:

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

Каждый `SqlCommentTrivia` содержит точный `Span`, исходный `Text`, `Kind` (`Line`/`Block`), `TokenIndex` и `Placement`: `Trailing` — после SQL-токена в той же строке, `Leading` — непосредственно перед следующим токеном (в том числе цепочка соседних комментариев), `Standalone` — без такого примыкания, например через пустую строку. `AnchorTokenIndex` указывает индекс связанного SQL-токена в `parsed.Tokens`; у `Standalone` он равен `null`. Сканер работает и с доступными токенами при ошибке разбора. Форматтер использует классификацию для строчных и блочных комментариев после запятых в `SELECT` и перед поддержанными предложениями; остальные позиции пока не получают отдельного правила размещения.

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

`FormatRequest` по умолчанию запрашивает весь документ (`Document`), диалект `Auto` и строгое поведение при ошибке разбора (`Strict`). Область `Selection` требует `SqlTextSpan`; `Selection` и `Statement` пока возвращают исходный текст с предупреждением `TSF3000`. В режиме `Strict` синтаксическая ошибка оставляет исходный текст без изменений и без правок: `ParseSucceeded == false`, одна или несколько диагностик `TSF1000` уровня `Error`. Если созданный форматтером SQL не проходит повторный разбор, исходный текст тоже сохраняется, а `TSF3001` возвращается как ошибка (`ParseSucceeded` при этом описывает успешный разбор исходного SQL).

Экспериментальный `Safe` доступен через `new FormatRequest(parseFailureBehavior: ParseFailureBehavior.Safe)`. Когда весь скрипт не разбирается, он пытается форматировать только отдельные операторы, найденные в частичном AST и успешно разобранные изолированно; ошибочные операторы и промежутки между ними остаются исходными. Результат может иметь `Changed == true` при `ParseSucceeded == false` и сохраняет диагностики `TSF1000`; итоговый скрипт всё ещё может быть синтаксически неверным. При полностью валидном SQL `Safe` работает как `Strict`. CLI всегда использует `Strict` и не записывает частичный результат. `TokenFallback` пока не реализован: запрос с ним возвращает `TSF3002` без изменений.

`FormattingOptions` разделяет параметры на `General` (ширина 100, LF, без конечного перевода строки), `Indent` (4 пробела, без табуляции), `Keywords` (`Upper`), `Select` (`Auto`) и `Clauses` (`Auto` для `GROUP BY` и `ORDER BY`). Для поддержанных `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE` применяются отступ и EOL; ширина и параметры раскладки списков влияют на поддержанные списки `SELECT`. В остальных случаях пока меняется только регистр ключевых слов. `FormatResult` содержит итоговый текст, правки `TextEdit`, диагностики, признаки изменения и успешности разбора.

`FormattingOptions.Default` предоставляет те же значения, что и `new FormattingOptions()`. Метод `With` создаёт новый набор настроек с заменой только указанных секций, не изменяя исходный:

```csharp
var options = FormattingOptions.Default.With(
    general: new GeneralOptions(maxLineWidth: 120),
    keywords: new KeywordOptions(KeywordCase.Lower));
```

Публичный набор секций сейчас ограничен реально поддержанными параметрами. Настройки `JOIN`, `CASE`, комментариев и других правил из плана пока не входят в API.

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

Пока структурно поддержаны простые списки колонок, одна таблица в `FROM`, базовый `WHERE`, `GROUP BY`, `HAVING` и `ORDER BY`. Строчные и блочные комментарии после запятых в списке колонок и перед предложениями поддержаны отдельно; при других комментариях внутри конструкции либо пока неподдержанном предложении исходное расположение сохраняется, но регистр ключевых слов всё равно может измениться. Завершающая точка с запятой сохраняется.

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

### Общие табличные выражения (CTE)

Поддержаны один или несколько простых CTE перед основным `SELECT`. Вложенный запрос каждого CTE получает отступ; список имён колонок, если он указан, сохраняется:

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

CTE с `XMLNAMESPACES`, вложенным `WITH` или неподдержанным выражением запроса пока сохраняет исходную раскладку. Поддержка CTE не распространяется на `INSERT`/`UPDATE`/`DELETE`.

### Подзапросы

Простой `SELECT` внутри скалярного подзапроса, производной таблицы, `EXISTS`, `IN` или `NOT IN` выводится отдельным блоком с отступом. Скалярный подзапрос поддержан в списке колонок и как сторона простого сравнения; предикаты с подзапросом работают в поддержанных условиях `WHERE`/`HAVING`/`ON`, в том числе рядом с `AND`/`OR`:

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

Скалярный подзапрос в списке колонок может иметь алиас; пример производной таблицы приведён ниже. Скобки и исходный регистр при `KeywordCase.Preserve` сохраняются. Комментарии внутри неподдержанных позиций не получают структурной раскладки: форматтер оставляет исходное расположение конструкции, хотя регистр ключевых слов может измениться.

### Выражения CASE

Простой `CASE значение WHEN ...` и поисковый `CASE WHEN условие ...` форматируются по ветвям в списке колонок `SELECT` и как сторона простого сравнения. `THEN` получает дополнительный отступ, `ELSE` необязателен:

```sql
SELECT
    CASE Status
        WHEN 1
            THEN 'New'
        ELSE 'Other'
    END AS Label
FROM T
```

Текст условий и результатов ветвей сохраняется. Если между частями `CASE` есть комментарий или другая неподдержанная конструкция, исходная раскладка этого запроса сохраняется; регистр ключевых слов может измениться.

### Операторы объединения запросов

`UNION`, `UNION ALL`, `INTERSECT` и `EXCEPT` ставятся на отдельную строку между поддержанными `SELECT`. Можно составлять цепочки, использовать их в производной таблице и добавлять итоговый `ORDER BY`:

```sql
SELECT Id
FROM A
UNION ALL
SELECT Id
FROM B
ORDER BY Id;
```

Если между запросами и оператором находится комментарий, расположение всего выражения сохраняется; измениться может только регистр ключевых слов. Сочетание такого выражения с CTE и `OFFSET/FETCH` пока структурно не форматируется.

### Оконные функции

Для вызова функции непосредственно в списке колонок `SELECT` предложение `OVER` с `PARTITION BY` и/или `ORDER BY` разбивается на строки. Списки разделов и сортировки сохраняют порядок; пустое `OVER()` становится `OVER ()`:

```sql
SELECT
    row_number() OVER (
        PARTITION BY Category
        ORDER BY CreatedAt DESC
    ) AS rn
FROM T
```

Аргументы самой функции сохраняют исходный текст. Оконные рамки (`ROWS`/`RANGE`), именованные окна и оконные функции внутри более сложного скалярного выражения пока не форматируются структурно. В таких случаях исходная раскладка сохраняется, но регистр распознанных ключевых слов может измениться.

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

Строчный комментарий непосредственно после запятой остаётся у предыдущей колонки. В этом случае список принудительно выводится по одной колонке на строку, даже при `Auto`. Например, `select Id, -- note\nName from T` становится:

```sql
SELECT
    Id, -- note
    Name
FROM T
```

Поддержаны несколько таких комментариев в одном списке. Пробел перед `--` нормализуется до одного, переводы строк создаются согласно `GeneralOptions.LineEnding`; сам текст комментария не меняется. Комментарии перед колонкой пока не получают отдельного правила размещения.

### Ведущие комментарии перед предложениями

Один или несколько соседних строчных или блочных комментариев непосредственно перед `FROM`, `WHERE`, `GROUP BY`, `HAVING` или `ORDER BY` сохраняются на отдельных строках перед соответствующим предложением:

```sql
SELECT Id
FROM T
-- фильтр
WHERE
    Id = 1
```

Комментарий перед самым `SELECT` тоже остаётся на месте. Комментарии внутри выражений и комментарии после кода в той же строке, кроме поддержанных запятых списка колонок, пока сохраняют исходную раскладку всего предложения.

### Блочные комментарии

`/* comment */` после запятой в списке колонок остаётся у предыдущей колонки, как и строчный комментарий:

```sql
SELECT
    Id, /* label */
    Name
FROM T
```

Блочный комментарий перед предложением, например `/* filter */` перед `WHERE`, также сохраняется при форматировании. Текст многострочного блочного комментария, включая его внутренние переводы строк, остаётся исходным. В остальных позициях форматтер сохраняет исходную раскладку вместо структурной переработки.

## INSERT

Поддержаны `INSERT [INTO] таблица [(колонки)] VALUES` с одной или несколькими строками значений и `INSERT [INTO] таблица [(колонки)] SELECT`. Целевые колонки нормализуются через запятую и пробел, каждая строка `VALUES` выводится отдельно:

```sql
INSERT INTO dbo.T (Id, Name)
VALUES
    (1, 'a'),
    (2, 'b');
```

В `INSERT ... SELECT` вложенный запрос использует те же поддержанные правила форматирования `SELECT`. Порядок колонок и значений сохраняется. `INSERT ... EXEC`, `DEFAULT VALUES`, CTE перед `INSERT` и комментарии между строками значений пока сохраняют исходную раскладку; распознанные ключевые слова всё ещё могут менять регистр.

## UPDATE

Базовый `UPDATE` с обычными присваиваниями `SET`, необязательными `FROM` и `WHERE` форматируется по предложениям. Каждое присваивание выводится на отдельной строке; в `FROM` поддержаны те же простые таблицы и соединения, что и для `SELECT`:

```sql
UPDATE t
SET
    Name = 'x',
    Count = 2
FROM dbo.T t
WHERE
    t.Id = 1;
```

Порядок присваиваний сохраняется. Составные присваивания вроде `+=`, `TOP` и CTE перед `UPDATE` пока не форматируются структурно. Для неподдержанных конструкций сохраняется исходная раскладка, хотя регистр распознанных ключевых слов может измениться.

## DELETE

Поддержаны простой `DELETE FROM таблица [WHERE ...]` и удаление по алиасу с `FROM` и `JOIN`. `FROM` и `WHERE` начинаются с новых строк, условие `WHERE` получает отступ:

```sql
DELETE t
FROM dbo.T t
JOIN dbo.U u
    ON t.Id = u.Id
WHERE
    u.Flag = 1;
```

`DELETE TOP` и CTE перед `DELETE` пока сохраняют исходную раскладку; регистр распознанных ключевых слов может измениться. Остальные ограничения `FROM` и `WHERE` совпадают с описанными для поддержанных `SELECT` и `UPDATE`.

## OUTPUT

В поддержанных `INSERT`, `UPDATE` и `DELETE` предложение `OUTPUT` выводится на отдельной строке. Список выражений разделяется запятой и пробелом; также поддержан `OUTPUT ... INTO таблица [(колонки)]`:

```sql
DELETE FROM T
OUTPUT deleted.Id INTO dbo.Audit (Id)
WHERE
    Id = 1;
```

Исходные выражения и их порядок сохраняются. Комментарий между элементами `OUTPUT` или неподдержанная форма цели `INTO` оставляют расположение всего оператора без структурной переработки. В поддержанном `MERGE` также можно использовать `OUTPUT` или `OUTPUT ... INTO`.

## MERGE

Базовый `MERGE` с именованными целевой таблицей и источником разделяется на `MERGE INTO`, `USING`, `ON` и ветви `WHEN ... THEN`. Поддержаны действия `UPDATE SET`, `INSERT ... VALUES` и `DELETE`, а также необязательное `OUTPUT`. Завершающая точка с запятой обязательна:

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

Ветвь `WHEN NOT MATCHED BY SOURCE THEN DELETE` тоже поддержана. Дополнительные условия `AND` у ветвей, составные присваивания, CTE перед `MERGE` и неподдержанные источники сохраняют исходную раскладку; регистр распознанных ключевых слов может измениться.

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

При неудачном разборе `BuildDocument` также возвращает неизменённый исходный текст. Для отдельных видов AST-узлов можно зарегистрировать собственные `ISqlFragmentDocBuilder`; более ранний обработчик имеет приоритет. `SqlFragmentWalker` позволяет обойти узлы ScriptDom. Встроенные структурные правила `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE` подключает `ScriptDomSqlFormatter`, а не пустой `SqlDocBuilder`.

## Ограничения

- CLI принимает stdin или один файл; `--write`, `--check` и поиск конфигурации доступны только для файла. Пользовательские флаги настроек пока не реализованы.
- Структурное форматирование охватывает только описанные формы `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE`; прочие конструкции сохраняют исходное расположение.
- Рендерер принимает готовое дерево `Doc`; сам по себе он не разбирает SQL.
