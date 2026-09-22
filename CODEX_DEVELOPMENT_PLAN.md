# T-SQL Formatter — план разработки для Codex

> Статус документа: рабочая спецификация проекта  
> Дата базовой версии: 2026-09-22  
> Основная цель: создать расширяемый formatter T-SQL с гибкими профилями форматирования и интеграциями с Visual Studio и SQL Server Management Studio (SSMS).

---

## 0. Как использовать этот документ в Codex

Этот файл является основным техническим планом проекта. При выполнении задач Codex должен:

1. Сначала прочитать этот документ целиком.
2. Определить текущую фазу и конкретную задачу.
3. Не реализовывать функциональность из следующих фаз без необходимости.
4. Делать небольшие, логически завершённые изменения.
5. Перед изменением публичных интерфейсов проверить, не нарушит ли это уже реализованные фазы.
6. Для любой новой функции formatter-а добавлять тесты до завершения задачи.
7. Не считать задачу завершённой, пока:
   - solution собирается;
   - тесты проходят;
   - formatter остаётся идемпотентным на добавленных сценариях;
   - комментарии и строковые литералы не повреждаются;
   - отсутствуют необоснованные изменения вне области текущей задачи.
8. Не копировать код, алгоритмы, конфигурационные форматы или ресурсы Redgate SQL Prompt / dbForge SQL Complete. Эти продукты используются только как функциональные и UX-референсы.
9. При неоднозначности выбирать самое простое решение, которое не закрывает возможность расширения архитектуры.
10. Если задача требует изменения архитектурного решения из раздела ADR, сначала явно обновить соответствующий ADR или создать новый.

Рекомендуемый рабочий цикл:

```text
read plan
→ inspect repository
→ define smallest implementation step
→ implement
→ add/update tests
→ run build
→ run tests
→ run formatter golden tests
→ summarize changes
```

---

# 1. Цель продукта

Разработать formatter для T-SQL, который:

- понимает синтаксическую структуру T-SQL;
- позволяет очень гибко настраивать стиль;
- корректно сохраняет комментарии и литералы;
- умеет форматировать весь документ, выделенный фрагмент и отдельный statement;
- предоставляет preview настроек;
- может работать независимо от IDE;
- интегрируется с Visual Studio;
- интегрируется с SSMS по best-effort модели;
- может использоваться из CLI и CI/CD;
- поддерживает командные профили форматирования в репозитории.

Концептуально продукт должен состоять не из «форматтера для Visual Studio», а из независимого движка:

```text
                   ┌─────────────────────┐
                   │ TSqlFormatter.Core  │
                   └──────────┬──────────┘
                              │
             ┌────────────────┼────────────────┐
             │                │                │
           CLI           Visual Studio       SSMS
             │
             └──── CI / pre-commit / scripts
```

---

# 2. Основные архитектурные принципы

## 2.1. IDE не содержит бизнес-логику форматирования

Visual Studio и SSMS должны:

- получить текст;
- получить selection/caret;
- загрузить профиль;
- вызвать formatter;
- применить результат;
- восстановить caret/selection;
- показать ошибки/диагностику.

Они НЕ должны самостоятельно решать:

- где ставить перенос строки;
- как форматировать JOIN;
- какой регистр использовать;
- как выравнивать alias;
- как форматировать CASE;
- как работать с CTE.

Вся эта логика находится в Core.

---

## 2.2. Parsing и rendering — разные задачи

Нельзя использовать подход:

```text
input SQL
→ AST
→ вручную собрать SQL строками
```

Нужен pipeline:

```text
source text
→ ScriptDom tokens
→ ScriptDom AST
→ normalized internal representation
→ formatting document/layout model
→ renderer
→ formatted text
→ diff/text edits
```

---

## 2.3. Форматирование должно быть идемпотентным

Обязательное свойство:

```text
Format(Format(sql)) == Format(sql)
```

Идемпотентность проверяется golden/property тестами.

---

## 2.4. Formatter не должен менять семантику SQL

Запрещено автоматически:

- переименовывать объекты;
- добавлять или удалять `DISTINCT`;
- менять порядок выражений;
- переписывать JOIN;
- изменять строковые литералы;
- изменять числа;
- менять quoted identifiers;
- удалять hints;
- преобразовывать `!=` в `<>` без отдельной явно включённой функции;
- менять `ISNULL` на `COALESCE`;
- делать semantic refactoring под видом форматирования.

Formatter меняет только layout и опционально casing там, где это безопасно.

---

# 3. Технологический baseline

## 3.1. Язык

Основной язык:

```text
C#
```

---

## 3.2. Парсер

Использовать:

```text
Microsoft.SqlServer.TransactSql.ScriptDom
```

ScriptDom использовать для:

- токенизации;
- parsing;
- построения `TSqlFragment`;
- определения границ statements/fragments;
- получения позиций токенов;
- классификации ключевых слов;
- работы с версиями T-SQL.

Не использовать ScriptDom generator как основной formatter.

Причина: продукт должен поддерживать значительно более гибкие layout-правила.

---

## 3.3. Target frameworks

Базовое архитектурное решение:

```text
TSqlFormatter.Core
    netstandard2.0
    + при необходимости net8.0 multi-target

TSqlFormatter.Configuration
    netstandard2.0
    + при необходимости net8.0 multi-target

TSqlFormatter.Cli
    net8.0

TSqlFormatter.Benchmarks
    net8.0

Visual Studio integration
    target определяется официальным VSSDK template/spike

SSMS integration
    target определяется отдельным compatibility spike
```

Причина использования `netstandard2.0` в Core — сохранение возможности подключить движок к in-process extension-хостам с более старым runtime.

На момент составления документа текущий пакет ScriptDom поддерживает .NET Standard 2.0, .NET 8.0 и .NET Framework 4.7.2.

Не повышать минимальный runtime Core без отдельного ADR.

---

## 3.4. Visual Studio extensibility

Для production-интеграции первым кандидатом считать:

```text
VSSDK + MEF editor APIs
```

Причины:

- наиболее полный extensibility surface;
- editor extensions традиционно используют MEF;
- подходит для команд, options/tool windows и editor buffer;
- лучше соответствует задаче интеграции с SSMS.

`VisualStudio.Extensibility` рассматривать позже отдельным spike. На дату этого документа он остаётся preview-направлением и не должен быть единственной основой production-архитектуры.

---

## 3.5. SSMS

SSMS integration считать:

```text
experimental / best-effort
```

Третьесторонние расширения SSMS официально не поддерживаются Microsoft, хотя их загрузка не блокируется.

Следствия:

- Core не должен зависеть от SSMS;
- SSMS adapter должен быть изолирован;
- поломка SSMS adapter не должна блокировать CLI/VS;
- требуется отдельный integration test checklist для каждой поддерживаемой версии SSMS;
- нельзя строить архитектуру продукта вокруг недокументированных API SSMS.

---

# 4. Структура solution

Целевая структура:

```text
TSqlFormatter.sln

src/
  TSqlFormatter.Core/
    Parsing/
    Syntax/
    Trivia/
    Formatting/
      Documents/
      Rules/
      Builders/
      Rendering/
    Diagnostics/
    Text/
    Abstractions/

  TSqlFormatter.Configuration/
    Models/
    Profiles/
    Serialization/
    Validation/
    EditorConfig/

  TSqlFormatter.Cli/
    Commands/
    IO/
    Reporting/

  TSqlFormatter.VisualStudio/
    Commands/
    Editor/
    Options/
    Profiles/
    Preview/
    Package/

  TSqlFormatter.Ssms/
    Commands/
    Editor/
    Compatibility/

tests/
  TSqlFormatter.Core.Tests/
  TSqlFormatter.Configuration.Tests/
  TSqlFormatter.GoldenTests/
    Cases/
  TSqlFormatter.Cli.Tests/
  TSqlFormatter.Architecture.Tests/
  TSqlFormatter.IntegrationTests/

benchmarks/
  TSqlFormatter.Benchmarks/

samples/
  sql/

docs/
  architecture/
  formatting-rules/
  adr/
```

Допустимо упростить структуру в первых фазах, но зависимости должны идти только в следующем направлении:

```text
Core
↑
Configuration
↑
CLI / VisualStudio / SSMS
```

Запрещены зависимости:

```text
Core → VisualStudio
Core → SSMS
Core → CLI
Configuration → VisualStudio
```

---

# 5. Базовые публичные интерфейсы

Конкретные сигнатуры могут уточняться, но архитектурно нужны следующие контракты.

## 5.1. Formatter

```csharp
public interface ISqlFormatter
{
    FormatResult Format(
        string source,
        FormattingOptions options,
        FormatRequest request,
        CancellationToken cancellationToken = default);
}
```

---

## 5.2. FormatRequest

```csharp
public sealed record FormatRequest
{
    public FormatScope Scope { get; init; }

    public TextSpan? Selection { get; init; }

    public SqlDialectVersion Dialect { get; init; }

    public ParseFailureBehavior ParseFailureBehavior { get; init; }
}
```

Scope:

```text
Document
Selection
Statement
```

---

## 5.3. FormatResult

```csharp
public sealed record FormatResult
{
    public string Text { get; init; }

    public IReadOnlyList<TextEdit> Edits { get; init; }

    public IReadOnlyList<FormatterDiagnostic> Diagnostics { get; init; }

    public bool Changed { get; init; }

    public bool ParseSucceeded { get; init; }
}
```

Core должен уметь вернуть:

- итоговый текст;
- либо минимальные edits;
- diagnostics;
- признак изменения.

---

## 5.4. Parser abstraction

```csharp
public interface ISqlParser
{
    SqlParseResult Parse(
        string source,
        SqlDialectVersion dialect,
        CancellationToken cancellationToken = default);
}
```

`SqlParseResult` должен содержать:

```text
source
TSqlFragment/root
token stream
parse errors
line map
```

---

# 6. Pipeline formatter-а

Полный pipeline:

```text
1. Normalize input metadata
2. Tokenize
3. Parse
4. Build token/trivia map
5. Resolve requested formatting scope
6. Build formatting context
7. Visit AST
8. Build layout document tree
9. Render with width/indent settings
10. Preserve/attach trivia
11. Validate result
12. Calculate text edits
13. Return result + diagnostics
```

---

# 7. Parsing layer

## 7.1. Обязанности

Parsing layer отвечает только за:

- выбор T-SQL parser version;
- получение AST;
- получение tokens;
- преобразование ScriptDom errors в собственные diagnostics;
- построение line/offset map;
- предоставление helper API для получения fragment spans.

Parsing layer не форматирует текст.

---

## 7.2. Версии SQL Server

Сразу заложить enum:

```text
Auto
Sql2016
Sql2017
Sql2019
Sql2022
Latest
```

Конкретное отображение на `SqlVersion` держать в одном месте.

MVP может использовать один default parser, но API сразу не должен блокировать поддержку нескольких версий.

---

## 7.3. Parse errors

Поддержать режимы:

```text
Strict
Safe
TokenFallback
```

### Strict

Если parser вернул syntax errors:

```text
не изменять текст
вернуть diagnostics
```

### Safe

Попытаться форматировать только полностью валидный fragment/statement.

### TokenFallback

Разрешить только ограниченные безопасные операции:

- keyword casing;
- normalizing очевидных пробелов;
- нормализация line endings по явной настройке.

В MVP реализовать `Strict`.

`Safe` и `TokenFallback` — после стабильного Core.

---

# 8. Token и Trivia model

Это критически важная часть проекта.

## 8.1. Trivia

Под trivia понимать:

```text
whitespace
new lines
single-line comments
block comments
```

String literals trivia НЕ являются.

---

## 8.2. Требование к комментариям

Комментарий должен сохранять логическую связь с окружающим кодом.

Пример:

```sql
SELECT
    c.Id, -- customer id
    c.Name
FROM dbo.Customer AS c
-- active customers only
WHERE c.IsActive = 1;
```

После форматирования нельзя получить:

```sql
-- customer id
SELECT ...
```

если комментарий относился к `c.Id`.

---

## 8.3. Начальная стратегия attachment

Для MVP определить:

```text
Leading comment
Trailing comment
Standalone comment
```

### Trailing

Комментарий после meaningful token в той же строке:

```sql
c.Id, -- id
```

привязывается к предыдущему синтаксическому элементу.

### Leading

Комментарий непосредственно перед statement/clause/node:

```sql
-- active customers
WHERE ...
```

привязывается к следующему логическому узлу.

### Standalone

Комментарий между крупными блоками остаётся самостоятельным layout element.

---

## 8.4. Нельзя терять исходные токены

Для каждого форматируемого fragment-а должны быть доступны:

```text
FirstTokenIndex
LastTokenIndex
StartOffset
FragmentLength
ScriptTokenStream
```

---

# 9. Internal syntax/context model

Не требуется полностью копировать AST ScriptDom.

Создать лёгкие wrapper/helper структуры только там, где ScriptDom неудобен:

```text
FormattingContext
FragmentContext
ClauseContext
TokenContext
CommentAttachment
IndentContext
```

Не создавать второй полноценный SQL parser.

---

# 10. Layout engine

Layout engine — ключевая часть продукта.

Он должен отделить решение:

```text
что выводить
```

от решения:

```text
помещается ли это в строку
```

---

## 10.1. Базовые document nodes

Минимальный набор:

```text
Text
Concat
Line
SoftLine
HardLine
Indent
Group
IfBreak
Align
Empty
```

Возможная модель:

```csharp
abstract record Doc;

record TextDoc(string Text) : Doc;

record ConcatDoc(
    IReadOnlyList<Doc> Children) : Doc;

record LineDoc(LineKind Kind) : Doc;

record IndentDoc(
    int Levels,
    Doc Content) : Doc;

record GroupDoc(
    Doc Content) : Doc;
```

---

## 10.2. Семантика Line

```text
SoftLine
    пробел в flat mode
    newline в broken mode

Line
    newline либо configurable break

HardLine
    всегда newline
```

---

## 10.3. Group

`Group` пытается уместить содержимое в текущую строку.

Пример:

```sql
SELECT Id, Name
```

при маленьком выражении.

Если превышен `MaxLineLength`:

```sql
SELECT
    Id,
    Name
```

---

## 10.4. Renderer

Renderer получает:

```text
Doc
FormattingOptions
```

и строит строку.

Renderer не должен знать AST.

Renderer отвечает за:

- indentation;
- line width;
- EOL;
- trimming trailing whitespace;
- tabs/spaces;
- final newline.

---

# 11. FormattingOptions

Не создавать сотни bool-полей в одном классе.

Использовать секции:

```csharp
public sealed record FormattingOptions
{
    public GeneralOptions General { get; init; }
    public KeywordOptions Keywords { get; init; }
    public IndentOptions Indent { get; init; }
    public SelectOptions Select { get; init; }
    public JoinOptions Joins { get; init; }
    public ExpressionOptions Expressions { get; init; }
    public CaseOptions Case { get; init; }
    public CteOptions Cte { get; init; }
    public DmlOptions Dml { get; init; }
    public CommentOptions Comments { get; init; }
    public AlignmentOptions Alignment { get; init; }
}
```

---

# 12. Первоначальный формат конфигурации

Файл проекта:

```text
.tsqlformatter.json
```

Пример:

```json
{
  "version": 1,
  "general": {
    "maxLineLength": 120,
    "lineEnding": "auto",
    "finalNewLine": true
  },
  "indent": {
    "style": "spaces",
    "size": 4
  },
  "keywords": {
    "case": "upper"
  },
  "select": {
    "columns": "auto",
    "commaStyle": "trailing"
  },
  "joins": {
    "clauseNewLine": true,
    "conditionNewLine": true
  },
  "expressions": {
    "booleanOperatorPosition": "leading"
  },
  "aliases": {
    "useAs": true
  }
}
```

---

# 13. Configuration precedence

Предусмотреть:

```text
built-in defaults
        ↓
global user profile
        ↓
solution/repository .tsqlformatter.json
        ↓
explicit CLI options
```

Для IDE:

```text
built-in defaults
        ↓
user settings
        ↓
repository config
        ↓
active named profile
```

Конфликт должен разрешаться детерминированно.

---

# 14. Profiles

Поддержать:

```text
Default
Compact
Expanded
Custom user profiles
Project profile
```

Profile:

```csharp
public sealed record FormattingProfile
{
    public string Id { get; init; }
    public string Name { get; init; }
    public FormattingOptions Options { get; init; }
}
```

MVP может иметь только Default + JSON file.

UI профилей реализовать позже.

---

# 15. MVP formatting rules

## 15.1. General

Реализовать:

- tabs/spaces;
- indent width;
- line ending;
- final newline;
- max line length;
- trailing whitespace removal;
- keyword casing.

---

## 15.2. SELECT

Поддержать:

```text
SELECT
DISTINCT
TOP
column expressions
aliases
comma placement
FROM
```

Варианты columns:

```text
Auto
OnePerLine
PreserveWhenPossible
```

Comma:

```text
Trailing
Leading
```

---

## 15.3. JOIN

Поддержать:

```text
INNER JOIN
LEFT JOIN
LEFT OUTER JOIN
RIGHT JOIN
FULL JOIN
CROSS JOIN
CROSS APPLY
OUTER APPLY
```

Опции:

```text
JOIN on new line
ON on same/new line
indent ON expression
each AND condition on new line
```

---

## 15.4. WHERE / boolean expressions

Поддержать:

```text
AND
OR
NOT
parentheses
comparison expressions
IN
BETWEEN
LIKE
IS NULL
EXISTS
```

Позиция operator:

```text
leading

WHERE
    A = 1
    AND B = 2

trailing

WHERE
    A = 1 AND
    B = 2
```

Для MVP реализовать `leading`.

---

## 15.5. GROUP BY / HAVING / ORDER BY

Поддержать:

- один элемент;
- несколько элементов;
- one-per-line;
- auto wrapping;
- ASC/DESC preservation.

---

## 15.6. CTE

Поддержать:

```sql
WITH Customers AS
(
    ...
),
Orders AS
(
    ...
)
SELECT ...
```

Опции позже:

```text
parenthesis position
comma leading/trailing
blank line after CTE block
indent inner SELECT
```

---

## 15.7. Subqueries

Поддержать:

```text
scalar subquery
IN subquery
EXISTS
derived tables
nested SELECT
```

---

## 15.8. CASE

Поддержать:

```sql
CASE
    WHEN ...
        THEN ...
    WHEN ...
        THEN ...
    ELSE ...
END
```

и compact-вариант для коротких CASE в последующих фазах.

---

## 15.9. DML

MVP:

```text
INSERT
UPDATE
DELETE
```

Следующая фаза:

```text
MERGE
OUTPUT
```

---

# 16. Formatting rules после MVP

После стабилизации MVP добавить:

```text
DECLARE
SET
IF / ELSE
WHILE
BEGIN / END
TRY / CATCH
THROW
RAISERROR
CREATE PROCEDURE
ALTER PROCEDURE
CREATE FUNCTION
ALTER FUNCTION
CREATE VIEW
ALTER VIEW
CREATE TABLE
ALTER TABLE
INDEX
MERGE
PIVOT
UNPIVOT
WINDOW
OVER
PARTITION BY
XML
JSON
OPENJSON
temporal syntax
query hints
table hints
dynamic SQL formatting (opt-in)
```

Dynamic SQL по умолчанию НЕ форматировать внутри string literal.

---

# 17. Selection formatting

Поддержать три режима:

```text
Format Document
Format Selection
Format Statement
```

---

## 17.1. Format Document

Форматируется весь editor buffer.

---

## 17.2. Format Selection

Алгоритм:

```text
selection
→ определить пересекающиеся ScriptDom fragments
→ найти минимальный безопасный форматируемый ancestor
→ отформатировать fragment
→ вернуть edit только этого диапазона
```

Если выделение находится посередине token/string literal:

```text
не форматировать автоматически
вернуть diagnostic
```

---

## 17.3. Format Statement

По позиции caret найти ближайший statement:

```text
SELECT
INSERT
UPDATE
DELETE
DECLARE
IF
...
```

и форматировать только его.

---

# 18. Text edits

Core должен уметь вычислять edits между:

```text
original
formatted
```

Не обязательно в первой реализации строить оптимальный diff.

Этапы:

### MVP

```text
одна замена format scope целиком
```

### Позже

```text
минимальный набор TextEdit
```

Причины minimal edits:

- стабильный caret;
- нормальный Undo;
- меньше конфликтов с editor;
- меньше side effects;
- лучше совместимость с другими extensions.

---

# 19. Caret и selection mapping

IDE adapter должен сохранять позицию пользователя.

Подход:

```text
old caret offset
→ TextEdit map
→ new caret offset
```

То же для selection:

```text
old start/end
→ mapped start/end
```

Это задача integration layer, а не renderer.

---

# 20. Visual Studio extension

## 20.1. Spike до production implementation

Создать отдельный proof-of-concept:

```text
VS-SPIKE-001
```

Цель:

- создать пустой VSIX;
- зарегистрировать команду;
- получить active text view;
- прочитать `.sql` document;
- заменить selection;
- проверить Undo;
- проверить caret;
- проверить installation в VS 2026.

Spike не должен содержать formatter logic.

---

## 20.2. Команды MVP

Добавить:

```text
Format T-SQL Document
Format T-SQL Selection
Format T-SQL Statement
```

---

## 20.3. Context visibility

Команды показывать/активировать только для:

```text
.sql
```

или editor buffers, которые явно распознаны как SQL/T-SQL.

Не применять formatter автоматически ко всем text files.

---

## 20.4. Keyboard shortcuts

Не захватывать агрессивно стандартные сочетания IDE.

Предоставить configurable commands.

---

## 20.5. Options UI

Первый этап:

```text
General
Keyword casing
Indent
SELECT
JOIN
WHERE
```

Позже расширить.

---

## 20.6. Preview

Options UI должен иметь SQL sample и live preview:

```text
option changed
→ debounce
→ Core.Format(sample)
→ update preview
```

UI не реализует правила самостоятельно.

---

## 20.7. Format on Save

Добавить после стабилизации manual formatting.

Параметр:

```text
Off
Current document
Only when project config exists
```

Default:

```text
Off
```

---

## 20.8. Format on Paste

Добавить значительно позже.

Требования:

- opt-in;
- не форматировать строковые литералы;
- определять валидный pasted fragment;
- минимизировать latency.

---

# 21. SSMS extension

## 21.1. Spike

До полноценной разработки выполнить:

```text
SSMS-SPIKE-001
```

Проверить:

- возможно ли загрузить extension;
- получить текущий SQL editor buffer;
- выполнить command;
- заменить text;
- Undo;
- selection;
- multiple query windows;
- stability после restart;
- installation/uninstallation.

---

## 21.2. Поддержка версий

Не использовать формулировку:

```text
supports all SSMS versions
```

Хранить compatibility matrix:

```text
SSMS version | tested | command | selection | options | known issues
```

---

## 21.3. Архитектурный принцип

Код:

```text
TSqlFormatter.Ssms
```

должен быть тонким adapter.

Если окажется возможно переиспользовать часть Visual Studio integration, вынести общие компоненты в:

```text
TSqlFormatter.EditorIntegration
```

только после появления реального дублирования.

Не создавать такой проект заранее.

---

# 22. CLI

CLI нужен до IDE extensions.

Executable:

```text
tsqlformat
```

---

## 22.1. Команды MVP

```bash
tsqlformat file.sql
```

Вывод formatted SQL в stdout.

```bash
tsqlformat file.sql --write
```

Перезаписать файл.

```bash
tsqlformat file.sql --check
```

Exit code:

```text
0 = already formatted
1 = formatting required
2 = parse/config/error
```

---

## 22.2. Directory mode

После MVP:

```bash
tsqlformat ./sql --recursive --write
```

---

## 22.3. stdin/stdout

Поддержать:

```bash
cat query.sql | tsqlformat
```

Это позволит интегрировать formatter с другими редакторами.

---

# 23. CI usage

Пример:

```bash
tsqlformat ./database --recursive --check
```

CI должен падать, если SQL требует форматирования.

Позже добавить:

```text
GitHub Action
Azure DevOps example
pre-commit hook
```

Не делать отдельные CI plugins до стабильного CLI.

---

# 24. Golden tests

Основная форма тестирования formatter-а.

Структура:

```text
tests/TSqlFormatter.GoldenTests/Cases/

select/
  simple.input.sql
  simple.expected.sql
  joins.input.sql
  joins.expected.sql

where/
cte/
case/
insert/
update/
delete/
comments/
invalid/
```

Каждая пара:

```text
*.input.sql
*.expected.sql
```

---

## 24.1. Golden test procedure

Для каждого case:

```text
input
→ Format
→ compare exact text with expected
→ Format(result)
→ compare again with result
```

Таким образом один тест одновременно проверяет output и идемпотентность.

---

## 24.2. Update snapshots

Не разрешать тестам автоматически обновлять expected output при обычном запуске.

Создать отдельную явную команду/tool:

```text
--update-golden
```

если понадобится.

---

# 25. Unit tests

Отдельно тестировать:

```text
parser mapping
line map
token navigation
comment attachment
layout renderer
line width decisions
configuration merging
configuration validation
text edit mapping
```

---

# 26. Property/invariant tests

Добавить инварианты:

## 26.1. Idempotency

```text
F(F(x)) == F(x)
```

---

## 26.2. Literals preserved

Последовательность string/numeric/binary literals до и после должна совпадать, кроме случаев, когда есть явно включённое правило, меняющее presentation безопасным способом.

В MVP presentation literals не менять вообще.

---

## 26.3. Comments preserved

Количество и текст comments должны сохраняться.

---

## 26.4. Parse validity

Если input успешно парсился:

```text
formatted output тоже должен успешно парситься
```

Если output не парсится:

```text
formatter bug
```

---

# 27. Regression corpus

Создать:

```text
tests/corpus/
```

Включить:

- реальные anonymized SQL samples;
- сложные procedures;
- nested CTE;
- много JOIN;
- CASE;
- window functions;
- comments;
- temp tables;
- dynamic SQL strings;
- malformed SQL.

Не добавлять proprietary SQL работодателя/клиента.

---

# 28. Performance

## 28.1. Targets

Ориентиры, не жёсткий SLA:

```text
1 000 lines    < 100 ms
10 000 lines   < 500 ms
50 000 lines   < 2 s
```

Измерять на фиксированном benchmark environment.

Сначала корректность, потом оптимизация.

---

## 28.2. BenchmarkDotNet

Создать benchmark scenarios:

```text
SmallSelect
MediumProcedure
LargeProcedure
LargeGeneratedScript
CommentHeavyScript
NestedQueries
```

Измерять:

```text
parse
layout build
render
total
allocations
```

---

## 28.3. Cancellation

Все IDE-level операции должны принимать `CancellationToken`.

Большой документ не должен блокировать UI thread.

---

# 29. Threading

Core:

```text
не должен зависеть от UI thread
```

IDE:

```text
получить snapshot
→ background formatting
→ switch to editor thread only for applying edits
```

Конкретную thread model определить по VSSDK API во время VS spike.

---

# 30. Diagnostics

Собственная модель:

```csharp
public sealed record FormatterDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    TextSpan? Span);
```

Коды:

```text
TSF1000 parsing
TSF2000 configuration
TSF3000 unsupported syntax
TSF4000 internal formatter
TSF5000 integration
```

Никогда не глотать exception молча.

---

# 31. Error handling

Formatter должен применять принцип:

```text
better no formatting than corrupted SQL
```

Если formatter не уверен, он должен:

```text
return unchanged source
+ diagnostic
```

а не выдавать частично повреждённый результат.

---

# 32. Logging

Core не должен зависеть от конкретного logging framework.

Разрешить abstraction либо optional callback.

IDE/CLI может подключать свой logger.

Не логировать SQL source по умолчанию — в нём могут быть конфиденциальные данные.

---

# 33. Formatting rule architecture

Не создавать один огромный `SqlFormatterVisitor`.

Разделять крупные конструкции.

Пример:

```text
SelectStatementFormatter
QuerySpecificationFormatter
FromClauseFormatter
JoinFormatter
WhereClauseFormatter
BooleanExpressionFormatter
CaseExpressionFormatter
CteFormatter
InsertFormatter
UpdateFormatter
DeleteFormatter
```

При этом не обязательно вводить `IFormattingRule` для каждого мелкого token.

Избегать переусложнения DI.

---

# 34. Visitor strategy

ScriptDom visitor можно использовать для обхода, но output должен строиться через layout abstraction.

Пример концепции:

```csharp
Doc FormatSelect(QuerySpecification node, FormattingContext context)
{
    ...
}
```

Предпочтительно, чтобы formatter methods возвращали `Doc`, а не писали напрямую в `StringBuilder`.

---

# 35. Keyword casing

Опции:

```text
Upper
Lower
Preserve
```

Default:

```text
Upper
```

Менять casing только для токенов, подтверждённых как keywords.

Не делать:

```text
string.Replace("select", "SELECT")
```

---

# 36. Identifier casing

MVP:

```text
Preserve
```

Не пытаться автоматически приводить имена таблиц/колонок к upper/lower без metadata database.

Позже можно добавить unsafe/explicit option.

---

# 37. Alias formatting

Опции:

```text
Preserve
RequireAs
RemoveAs
```

MVP:

```text
Preserve
```

Поскольку добавление/удаление `AS` — это уже token-level rewrite, его лучше вводить после стабильного layout engine.

---

# 38. Parentheses

MVP:

```text
preserve semantic parentheses
```

Не удалять существующие parentheses автоматически.

Formatter может менять их положение относительно переносов, но не semantic structure.

---

# 39. Blank lines

MVP:

- убрать лишние trailing whitespace;
- ограниченно нормализовать blank lines между statements.

Не уничтожать намеренно оставленные большие блоки whitespace внутри comments/string literals.

Позже добавить:

```text
blank lines between statements
blank lines before major clauses
blank line around CTE
blank line around BEGIN/END blocks
```

---

# 40. Comments formatting

MVP:

```text
preserve comment text
preserve comment type
reattach position safely
```

Не делать automatic reflow текста комментария.

Не менять:

```text
-- TODO
/* block */
```

в другой тип.

---

# 41. `.editorconfig`

Не включать в первый MVP.

После стабилизации JSON configuration добавить limited mapping:

```ini
[*.sql]
tsql_formatter_indent_size = 4
tsql_formatter_keyword_case = upper
tsql_formatter_max_line_length = 120
```

Не пытаться сразу перенести все настройки formatter-а в `.editorconfig`.

---

# 42. Versioning config

JSON должен иметь:

```json
{
  "version": 1
}
```

При неизвестной major/version:

```text
diagnostic
do not silently reinterpret
```

---

# 43. Schema

После стабилизации config model публиковать:

```text
JSON Schema
```

для autocomplete в IDE.

---

# 44. Packaging

План:

```text
Core
    NuGet package, возможно позже

CLI
    dotnet tool / self-contained executable

Visual Studio
    VSIX

SSMS
    отдельный package/install mechanism после spike
```

Не объединять VS и SSMS installer, пока не подтверждена совместимость.

---

# 45. Security

Formatter работает локально.

MVP не требует:

```text
network access
cloud service
telemetry
account
API key
```

Это важно сохранить.

---

# 46. Telemetry

По умолчанию отсутствует.

Если когда-либо будет добавлена:

- только opt-in;
- не отправлять SQL;
- отдельный ADR;
- документировать payload.

---

# 47. Лицензирование и сторонние продукты

Разрешено изучать публично доступное поведение:

```text
Redgate SQL Prompt
dbForge SQL Complete
SSMS
Visual Studio
Prettier-like formatting concepts
```

Нельзя:

- декомпилировать proprietary extensions ради копирования реализации;
- копировать их internal schemas;
- копировать ресурсы/icons;
- копировать proprietary default profiles дословно;
- представлять продукт как официальный компонент Redgate/dbForge/Microsoft.

---

# 48. ADR

Хранить архитектурные решения:

```text
docs/adr/0001-use-scriptdom.md
docs/adr/0002-custom-layout-engine.md
docs/adr/0003-core-runtime-boundary.md
docs/adr/0004-vssdk-first.md
docs/adr/0005-ssms-best-effort.md
```

Формат ADR:

```text
Context
Decision
Consequences
Alternatives
```

---

# 49. Roadmap

---

## Phase 0 — Repository bootstrap

### P0-001 — Create solution

Создать:

```text
TSqlFormatter.sln
Core
Configuration
Cli
Core.Tests
GoldenTests
```

### Acceptance criteria

- solution build проходит;
- test projects запускаются;
- nullable включён;
- warnings настроены;
- README содержит команды build/test.

---

### P0-002 — Code quality baseline

Настроить:

```text
.editorconfig
Directory.Build.props
nullable
implicit usings policy
TreatWarningsAsErrors для собственного кода
```

Не превращать analyzer configuration в отдельный большой проект.

---

### P0-003 — Add ScriptDom

Добавить стабильный пакет ScriptDom.

Зафиксировать version centrally.

Создать smoke test:

```text
SELECT 1;
```

парсится без ошибок.

---

### P0-004 — ADR baseline

Создать ADR 0001–0005.

---

## Phase 1 — Parser foundation

### P1-001 — Parser abstraction

Реализовать:

```text
ISqlParser
SqlParseResult
SqlDialectVersion
ParseDiagnostic
```

### Acceptance criteria

- valid SELECT;
- invalid SELECT;
- multi-statement script;
- comments;
- quoted identifiers;
- string literals.

---

### P1-002 — Token helpers

Реализовать helper API:

```text
GetToken
GetPreviousMeaningfulToken
GetNextMeaningfulToken
GetFragmentTokens
GetTextSpan
```

---

### P1-003 — Line map

Offset ↔ line/column.

Тесты для:

```text
LF
CRLF
empty file
unicode
```

---

## Phase 2 — Layout engine

### P2-001 — Doc model

Реализовать:

```text
Text
Concat
SoftLine
HardLine
Indent
Group
IfBreak
```

---

### P2-002 — Basic renderer

Поддержать:

```text
indent
line endings
max width
final newline
```

---

### P2-003 — Renderer tests

Минимум 30 unit tests:

```text
nested group
nested indent
long token
empty doc
soft line
hard line
line width boundary
```

---

### P2-004 — Benchmark renderer

Создать первые microbenchmarks.

---

## Phase 3 — Formatting foundation

### P3-001 — Public formatter API

Реализовать:

```text
ISqlFormatter
FormatRequest
FormatResult
FormattingOptions
```

---

### P3-002 — Script visitor/builder infrastructure

Создать infrastructure для:

```text
AST node → Doc
```

Не реализовывать всё в одном visitor.

---

### P3-003 — Keyword casing

Первое реальное правило.

Тесты:

```text
select → SELECT
Select → SELECT
keyword inside string unchanged
keyword inside comment unchanged
identifier unchanged
```

---

### P3-004 — Basic SELECT

Поддержать:

```text
SELECT literal
SELECT column
SELECT multiple columns
FROM table
alias preserve
```

---

## Phase 4 — SELECT family MVP

### P4-001 — SELECT columns

```text
one-per-line
auto wrapping
trailing comma
```

---

### P4-002 — FROM

Поддержать:

```text
schema.table
alias
derived table basic
```

---

### P4-003 — JOIN

INNER/LEFT/RIGHT/FULL/CROSS/APPLY.

---

### P4-004 — WHERE

Basic comparisons + AND/OR.

---

### P4-005 — GROUP/HAVING/ORDER

Полная базовая поддержка.

---

### P4-006 — Parentheses

Nested boolean/query expressions.

---

### Phase 4 exit criteria

Следующий SQL форматируется стабильно:

```sql
SELECT
    c.Id,
    c.Name,
    SUM(o.Amount) AS TotalAmount
FROM dbo.Customer AS c
INNER JOIN dbo.[Order] AS o
    ON o.CustomerId = c.Id
WHERE
    c.IsActive = 1
    AND o.CreatedAt >= @DateFrom
GROUP BY
    c.Id,
    c.Name
HAVING
    SUM(o.Amount) > 0
ORDER BY
    TotalAmount DESC;
```

Повторный Format не меняет результат.

---

## Phase 5 — Comments and trivia

### P5-001 — Trivia scanner/model

Разделить:

```text
leading
trailing
standalone
```

---

### P5-002 — Inline comments

```sql
Id, -- comment
```

---

### P5-003 — Leading comments

```sql
-- comment
WHERE ...
```

---

### P5-004 — Block comments

Сохранение:

```sql
/* comment */
```

---

### P5-005 — Comment regression suite

Минимум 50 golden cases.

---

### Phase 5 exit criteria

Ни один golden test не теряет и не дублирует комментарий.

---

## Phase 6 — Advanced query expressions

### P6-001 — CTE

Single + multiple CTE.

### P6-002 — Subqueries

Scalar/derived/EXISTS/IN.

### P6-003 — CASE

Simple + searched CASE.

### P6-004 — UNION family

```text
UNION
UNION ALL
INTERSECT
EXCEPT
```

### P6-005 — Window functions

```text
OVER
PARTITION BY
ORDER BY
```

---

## Phase 7 — DML

### P7-001 — INSERT

```text
INSERT VALUES
INSERT SELECT
column lists
```

### P7-002 — UPDATE

```text
SET
FROM
WHERE
```

### P7-003 — DELETE

```text
FROM
JOIN
WHERE
```

### P7-004 — OUTPUT

### P7-005 — MERGE

После остальных DML.

---

## Phase 8 — Configuration

### P8-001 — Options model cleanup

Зафиксировать public options.

### P8-002 — JSON serializer

`.tsqlformatter.json`.

### P8-003 — Validation

Unknown/invalid values → diagnostics.

### P8-004 — Precedence

Defaults + file + CLI.

### P8-005 — Profiles

Named profiles.

---

## Phase 9 — CLI MVP

### P9-001 — stdin/stdout

### P9-002 — single file

### P9-003 — `--write`

### P9-004 — `--check`

### P9-005 — config discovery

Искать `.tsqlformatter.json` вверх от файла до repository/filesystem root по чётко определённому алгоритму.

### P9-006 — integration tests

---

### CLI MVP release criteria

```text
tsqlformat query.sql
tsqlformat query.sql --write
tsqlformat query.sql --check
```

работают предсказуемо.

---

## Phase 10 — Robustness

### P10-001 — Strict invalid SQL behavior

### P10-002 — Safe mode prototype

### P10-003 — Very large files

### P10-004 — cancellation

### P10-005 — fuzz/property tests

### P10-006 — crash corpus

Любой найденный crash → regression test.

---

## Phase 11 — Visual Studio spike

### P11-001 — Empty VSIX

### P11-002 — Editor command

### P11-003 — Read buffer

### P11-004 — Replace selection

### P11-005 — Undo/caret

### P11-006 — Background execution

По итогам создать:

```text
docs/architecture/visual-studio-integration.md
```

и ADR с подтверждёнными API.

---

## Phase 12 — Visual Studio MVP

### P12-001

Format Document.

### P12-002

Format Selection.

### P12-003

Format Statement.

### P12-004

Load project config.

### P12-005

Error notification.

### P12-006

VSIX packaging.

---

## Phase 13 — Settings UI

### P13-001 — General options

### P13-002 — SQL preview

### P13-003 — SELECT/JOIN/WHERE pages

### P13-004 — Profile selection

### P13-005 — Import/export profile

---

## Phase 14 — Visual Studio automation

### P14-001 — Format on Save

Default off.

### P14-002 — Save exclusions

### P14-003 — Format on Paste prototype

### P14-004 — performance/cancellation validation

---

## Phase 15 — SSMS spike

### P15-001 — load extension

### P15-002 — current query editor

### P15-003 — execute formatter command

### P15-004 — selection/caret/undo

### P15-005 — options

### P15-006 — compatibility matrix

После spike принять решение:

```text
GO
LIMITED SUPPORT
NO-GO
```

Если NO-GO, Core/CLI/VS продолжают развиваться независимо.

---

## Phase 16 — SSMS MVP

Только если spike успешен.

Реализовать:

```text
Format Document
Format Selection
Format Statement
config/profile loading
basic options
```

---

## Phase 17 — Stored code / control flow

Добавить:

```text
DECLARE
SET
IF
ELSE
BEGIN END
WHILE
TRY CATCH
THROW
procedures
functions
views
```

---

## Phase 18 — Alignment

Добавлять только после стабильного базового formatter-а.

Возможности:

```text
align aliases
align AS
align equals in SET
align data types in DECLARE
align assignment
```

Пример:

```sql
SELECT
    CustomerId   AS CustomerId,
    CustomerName AS CustomerName,
    CreatedAt    AS CreatedAt
```

Alignment не должен ухудшать поведение при длинных строках.

---

## Phase 19 — `.editorconfig`

Limited subset.

Не заменяет JSON profiles.

---

## Phase 20 — CI distribution

Добавить:

```text
dotnet tool
GitHub Actions example
Azure DevOps example
pre-commit example
```

---

# 50. Milestones

## M0 — Parser prototype

Включает Phase 0–1.

---

## M1 — Formatter prototype

Phase 2–4.

Умеет форматировать базовый SELECT/JOIN/WHERE.

---

## M2 — Usable Core

Phase 5–8.

Комментарии, CTE, CASE, DML, config.

---

## M3 — CLI release

Phase 9–10.

Уже можно реально использовать вне IDE.

---

## M4 — Visual Studio MVP

Phase 11–12.

---

## M5 — Visual Studio public beta

Phase 13–14.

---

## M6 — SSMS experimental beta

Phase 15–16.

---

## M7 — Advanced formatter

Phase 17+.

---

# 51. MVP scope

Первый реально используемый MVP должен включать:

```text
Core
ScriptDom parser
layout engine
keyword casing
SELECT
FROM
JOIN
WHERE
GROUP BY
HAVING
ORDER BY
CTE
subquery
CASE
INSERT
UPDATE
DELETE
comments
JSON config
CLI
golden tests
```

Не включать в MVP:

```text
SSMS
Visual Studio settings UI
format on paste
alignment
dynamic SQL formatting
database metadata
IntelliSense
code completion
SQL linting
semantic refactoring
```

---

# 52. Definition of Done для formatting rule

Новая formatting rule считается завершённой только если:

1. Есть setting/default behavior, если настройка требуется.
2. Есть unit или golden tests.
3. Есть тест минимального выражения.
4. Есть тест multiline выражения.
5. Есть тест nested expression.
6. Есть тест комментария рядом с конструкцией.
7. Есть idempotency check.
8. Output успешно парсится.
9. String/comment text не изменён.
10. Нет formatter exception на malformed соседнем input.

---

# 53. Definition of Done для Codex task

Перед завершением каждой задачи Codex должен сообщить:

```text
Implemented
Tests added/changed
Commands executed
Known limitations
Next logical task
```

И реально выполнить доступные команды build/test.

---

# 54. Правила внесения изменений Codex

## Делать

- читать существующие abstractions;
- переиспользовать уже созданные primitives;
- добавлять небольшие классы с одной ролью;
- писать regression test до исправления formatter bug;
- сохранять обратную совместимость config;
- обновлять documentation при изменении поведения.

## Не делать

- переписывать весь formatter ради одной rule;
- добавлять reflection без необходимости;
- создавать собственный SQL parser;
- выполнять regex-formatting SQL как основной механизм;
- использовать regex для распознавания comments/string literals вместо token stream;
- добавлять database connection в Core;
- добавлять network calls;
- форматировать SQL через внешний online service;
- менять literals;
- глотать parser errors;
- форматировать invalid SQL в unsafe режиме по умолчанию;
- смешивать VS APIs с Core.

---

# 55. Coding conventions

Базово:

```text
nullable enable
async suffix for async methods
CancellationToken последним параметром
records для immutable result/options DTO, где уместно
interfaces только на реальных abstraction boundaries
internal по умолчанию
public только для API
```

Избегать:

```text
Utils.cs
Helpers.cs на сотни методов
Manager классов без конкретной роли
Service locator
global static mutable state
```

---

# 56. Naming

Основной namespace:

```text
TSqlFormatter
```

Примеры:

```text
TSqlFormatter.Core
TSqlFormatter.Core.Parsing
TSqlFormatter.Core.Formatting
TSqlFormatter.Configuration
TSqlFormatter.VisualStudio
```

Если перед публикацией будет выбрано продуктовое название, namespaces можно изменить отдельной migration-задачей.

---

# 57. Test naming

Пример:

```text
Format_SelectWithTwoColumns_PutsColumnsOnSeparateLines
Format_StringContainingSelect_DoesNotChangeLiteral
Format_TrailingComment_PreservesCommentAfterColumn
Format_AlreadyFormattedQuery_ReturnsNoChanges
```

Golden filename:

```text
select.two-columns.trailing-comma
join.left.multiple-conditions
comments.trailing-column
cte.multiple
case.nested
```

---

# 58. Baseline sample

Использовать как один из первых integration golden cases:

```sql
with customer_orders as(select c.id,c.name,sum(o.amount) total from dbo.customer c left join dbo.orders o on o.customer_id=c.id and o.deleted=0 where c.active=1 and o.created_at>=@datefrom group by c.id,c.name) select id,name,total from customer_orders where total>1000 order by total desc;
```

Ожидаемый базовый style:

```sql
WITH customer_orders AS
(
    SELECT
        c.id,
        c.name,
        SUM(o.amount) AS total
    FROM dbo.customer AS c
    LEFT JOIN dbo.orders AS o
        ON o.customer_id = c.id
        AND o.deleted = 0
    WHERE
        c.active = 1
        AND o.created_at >= @datefrom
    GROUP BY
        c.id,
        c.name
)
SELECT
    id,
    name,
    total
FROM customer_orders
WHERE
    total > 1000
ORDER BY
    total DESC;
```

Важно: добавление `AS` в этом sample отражает желаемый будущий style. Пока alias rewrite не реализован безопасно, golden test должен использовать режим `Preserve` либо input с уже существующим `AS`.

---

# 59. Первые задачи для Codex

Если repository пустой, начинать строго в таком порядке:

```text
1. P0-001 Create solution
2. P0-002 Code quality baseline
3. P0-003 Add ScriptDom + smoke test
4. P0-004 ADR baseline
5. P1-001 Parser abstraction
6. P1-002 Token helpers
7. P1-003 Line map
8. P2-001 Doc model
9. P2-002 Basic renderer
10. P2-003 Renderer tests
```

Не начинать Visual Studio extension до работающего formatter prototype.

---

# 60. Рекомендуемый первый prompt для Codex

```text
Прочитай CODEX_DEVELOPMENT_PLAN.md целиком.

Начни только с задачи P0-001 — Create solution.

Создай минимальную структуру solution и проектов, описанную в плане.
Не реализуй parser, formatter, CLI commands или Visual Studio extension.
Не добавляй лишние зависимости.

После изменений:
1. собери solution;
2. запусти тесты;
3. перечисли созданные проекты;
4. укажи команды, которые выполнил;
5. не переходи к P0-002.
```

Следующий prompt:

```text
Прочитай CODEX_DEVELOPMENT_PLAN.md и текущее состояние repository.

Выполни только P0-002 — Code quality baseline.
Не переходи к следующим фазам.

После изменений выполни build и tests и сообщи результат.
```

Такой способ предпочтительнее запроса вида:

```text
"реализуй весь formatter"
```

---

# 61. Open questions

Не блокируют старт Phase 0–4:

1. Финальное название продукта.
2. Marketplace distribution model.
3. Конкретная support matrix Visual Studio.
4. Конкретная support matrix SSMS.
5. Нужен ли standalone GUI.
6. Нужна ли VS Code integration.
7. Будет ли Core опубликован как NuGet.
8. Нужен ли коммерческий/opensource license.

До соответствующей фазы не принимать лишних решений.

---

# 62. Риски

## Высокий — comments/trivia

Основной технический риск formatter engine.

Mitigation:

```text
token stream
explicit attachment model
large golden corpus
```

---

## Высокий — SSMS extensibility

Microsoft официально не поддерживает third-party SSMS extensions.

Mitigation:

```text
isolated adapter
compatibility spike
no Core dependency
best-effort support
```

---

## Средний — layout complexity

Большое количество настроек может быстро сделать rules нечитаемыми.

Mitigation:

```text
Doc/layout algebra
small formatter components
golden tests
profiles
```

---

## Средний — malformed/incomplete SQL

Особенно при format-on-paste/live operation.

Mitigation:

```text
Strict default
Safe mode later
never corrupt source
```

---

## Средний — huge procedures

Mitigation:

```text
benchmarks
cancellation
background formatting
avoid excessive allocations
```

---

# 63. Архитектурные критерии успеха

Архитектура считается успешной, если:

- один и тот же Core используется CLI, VS и SSMS;
- Core можно тестировать без установленной Visual Studio;
- добавление formatting rule не требует менять IDE code;
- добавление IDE integration не требует менять formatting rules;
- config можно использовать одинаково из CLI и IDE;
- golden tests полностью воспроизводимы;
- formatter не зависит от SQL Server instance;
- formatter работает offline.

---

# 64. Product критерии успеха

MVP считается полезным, если разработчик может:

1. Создать `.tsqlformatter.json`.
2. Настроить indent, casing, SELECT, JOIN, WHERE.
3. Выполнить:

```bash
tsqlformat query.sql --write
```

4. Получить стабильный, повторяемый output.
5. Запустить:

```bash
tsqlformat ./sql --check
```

в CI.
6. Позже получить тот же результат через Visual Studio command.

---

# 65. Источники и технические замечания

Проверено при составлении плана:

- Microsoft ScriptDom предоставляет parsing и token stream API:
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom.tsqlparser

- ScriptDom token содержит offset, line, column, text и token type:
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom.tsqlparsertoken

- Visual Studio editor extensibility использует MEF для большого числа editor extensions:
  https://learn.microsoft.com/en-us/visualstudio/extensibility/extending-the-editor-and-language-services

- Microsoft описывает VSSDK как наиболее полный и мощный extensibility model:
  https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/extensibility-models

- Visual Studio 2026 имеет новую модель совместимости VSIX, при которой поддерживаемые VS 2022 API могут продолжать работать:
  https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility

- Third-party extensions для SSMS официально не поддерживаются:
  https://learn.microsoft.com/en-us/ssms/faq

- Текущий NuGet ScriptDom поддерживает .NET Framework 4.7.2, .NET Standard 2.0 и .NET 8:
  https://www.nuget.org/packages/microsoft.sqlserver.transactsql.scriptdom

Документация и версии инструментов меняются. Перед началом конкретной IDE integration phase Codex должен повторно проверить актуальные Microsoft docs, а не слепо полагаться на зафиксированную здесь версию API.

---

# 66. Финальный принцип проекта

Сначала построить качественный standalone formatter:

```text
Parser
→ Tokens/Trivia
→ AST
→ Layout
→ Renderer
→ Tests
→ CLI
```

и только затем подключать:

```text
Visual Studio
→ Settings UI
→ SSMS
```

Качество продукта определяется прежде всего предсказуемостью Core formatter-а, идемпотентностью, сохранностью SQL и гибкостью правил, а не количеством IDE-функций.
