using TSqlFormatter.Core.Formatting;

namespace TSqlFormatter.Configuration;

/// <summary>Human-facing labels; stable technical IDs remain only lookup/search identifiers.</summary>
public sealed class SettingsPresentation
{
    private readonly bool russian;
    private static readonly string[] pageIds = { "global.basics", "global.lists", "global.casingSpacing", "queries.select", "queries.joinsConditions", "queries.subqueriesUnion", "data.insert", "data.updateDelete", "data.merge", "schema.tablesViews", "schema.routinesTriggers", "schema.blocksVariables" };
    private static readonly IReadOnlyDictionary<string, string[]> words = Vocabulary.Split('\n')
        .Select(line => line.Trim()).Where(line => line.Length > 0).Select(line => line.Split('|'))
        .ToDictionary(parts => parts[0], parts => parts.Skip(1).ToArray(), StringComparer.Ordinal);

    public SettingsPresentation(bool russian = true) { this.russian = russian; }
    public string Text(string ru, string en) => russian ? ru : en;
    public string Word(string id) => words.TryGetValue(id, out var pair) ? pair[russian ? 0 : 1]
        : throw new ArgumentException("Missing human-facing label: " + id, nameof(id));

    public string GroupId(SettingsField field) => field.RuleKey ?? field.Id;
    public string Title(SettingsField field) => field.Id == "keywords.case" ? Text("Регистр ключевых слов", "Keyword casing") : Word(GroupId(field).Split('.').Last());
    public IReadOnlyList<string> Path(SettingsField field) => Array.AsReadOnly(GroupId(field).Split('.')
        .Take(GroupId(field).Split('.').Length - 1).Select(Word).ToArray());
    public string FieldLabel(SettingsField field) => field.Member is null ? Title(field) : Word(field.Member);
    public string ChoiceLabel(string value) => Word(value is "insert" or "on" ? "choice." + value : value);
    public string Context(SettingsField field) => string.Join(" → ", Path(field).Concat(new[] { Title(field) }));

    // Stable, compact navigation: a group and one page level. Catalog detail stays in the page.
    public string PageId(SettingsField field)
    {
        string key = GroupId(field);
        return field.Category switch
        {
            "general" or "indent" or "misc" => "global.basics",
            "stackedList" => "global.lists",
            "layout" => key.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("parenthes", StringComparison.OrdinalIgnoreCase) >= 0
                ? "global.lists" : "global.basics",
            "keywords" or "textCase" or "spacing" => "global.casingSpacing",
            "joins" or "where" => "queries.joinsConditions",
            "alignment" => key == "alignment.selectAliases" ? "queries.select" : key == "alignment.setAssignments" ? "data.updateDelete" : "schema.blocksVariables",
            "clauses" => "queries.select",
            "select" => key.StartsWith("select.join.", StringComparison.Ordinal) || key.StartsWith("select.where.", StringComparison.Ordinal) || key.StartsWith("select.having.", StringComparison.Ordinal)
                ? "queries.joinsConditions" : "queries.select",
            "subquery" or "setOperator" => "queries.subqueriesUnion",
            "insert" => "data.insert",
            "update" or "delete" => "data.updateDelete",
            "merge" => "data.merge",
            "createTable" or "view" => "schema.tablesViews",
            "routine" or "trigger" or "execute" => "schema.routinesTriggers",
            "code" or "declare" or "labels" or "case" => "schema.blocksVariables",
            _ => throw new ArgumentException("Missing navigation page for category: " + field.Category, nameof(field))
        };
    }
    public IReadOnlyList<string> PagePath(SettingsField field) => Array.AsReadOnly(PageId(field).Split('.').Select(id => Word("navigation." + id)).ToArray());
    public int PageOrder(SettingsField field) => Array.IndexOf(pageIds, PageId(field));
    public string PageTitle(SettingsField field) => PagePath(field)[1];
    public string SectionId(SettingsField field)
    {
        string key = GroupId(field);
        int dot = key.LastIndexOf('.');
        return dot < 0 ? key : key.Substring(0, dot);
    }
    public string SectionTitle(SettingsField field) => string.Join(" · ", SectionId(field).Split('.').Select(Word));
    public string PageFieldLabel(SettingsField field) => field.Member is null ? Title(field) : Title(field) + ": " + Word(field.Member);

    public IEnumerable<SettingsField> Find(SettingsEditorModel model, string? search)
    {
        var terms = (search ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return model.Fields.Where(field => terms.All(term =>
            (Context(field) + " " + FieldLabel(field) + " " + string.Join(" ", PagePath(field)) + " " + field.Id + " " + field.Scope)
                .IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
    }

    public string Describe(SettingsEditorModel model, SettingsField field)
    {
        string text = Context(field) + ". " + Text("Значение: ", "Value: ")
            + (model.IsOverridden(field.Id) ? Text("задано явно.", "explicit override.") : Text("по умолчанию / унаследовано.", "default / inherited."));
        if (field.Dependency is { } dependency)
        {
            var required = model.Fields.First(f => f.RuleKey == dependency);
            object value = model.Get(required.Id);
            string label = value is bool boolean ? Text(boolean ? "включено" : "выключено", boolean ? "enabled" : "disabled")
                : value is string choice ? ChoiceLabel(choice) : value.ToString();
            text += " " + Text("Зависит от: ", "Depends on: ") + Context(required) + " — " + label + ".";
        }
        if (field.RuleKey?.StartsWith("subquery.", StringComparison.Ordinal) == true && field.RuleKey != "subquery.useSelectFormatting")
            text += " " + Text("Для независимых правил подзапроса отключите наследование SELECT.", "Disable SELECT inheritance to use independent subquery rules.");
        if (field.RuleKey?.StartsWith("merge.update.set.", StringComparison.Ordinal) == true ||
            field.RuleKey?.StartsWith("merge.insert.", StringComparison.Ordinal) == true && field.RuleKey != "merge.insert.useStatementFormatting")
            text += " " + Text("Отключите наследование настроек UPDATE/INSERT в соответствующей ветви MERGE.", "Disable UPDATE/INSERT inheritance for the corresponding MERGE branch.");
        if (field.Member == "offset") text += " " + Text("Смещение задаётся в шагах общего отступа, не в пробелах.", "Offset is in global indent steps, not spaces.");
        if (field.Member == "transparent") text += " " + Text("Сохранять вложенные отступы при переносе блока.", "Retain nested indentation when moving a block.");
        text += " " + Text("Неактивные значения сохраняются для последующего использования.", "Inactive values are retained for later use.");
        if (field.Dialect is not null) text += " " + Text("Диалект: ", "Dialect: ") + field.Dialect + ".";
        return text;
    }

    public string ProfileName(FormattingProfile profile)
    {
        if (profile.Id.StartsWith("user:", StringComparison.Ordinal)) return profile.Name;
        string id = profile.Id.Split(':')[1];
        return id switch
        {
            "Default" => Text("Стандартный", "Standard"), "Compact" => Text("Компактный", "Compact"),
            "Expanded" => Text("Развёрнутый", "Expanded"), "ReadableVertical" => Text("Читаемый вертикальный", "Readable vertical"),
            "CompactQueries" => Text("Компактные запросы", "Compact queries"), _ => profile.Name
        };
    }

    public string Example(SettingsField field)
    {
        string key = GroupId(field);
        if (key.StartsWith("select.singleLine", StringComparison.Ordinal)) return "SELECT Id, Name FROM dbo.Items WHERE Id > 0;";
        if (key.StartsWith("textCase", StringComparison.Ordinal) || key.StartsWith("spacing", StringComparison.Ordinal))
            return "SELECT T.Id AS ItemId, [MixedName], @Value + 2 AS Amount, COUNT(*) AS Total, GETDATE(), geometry::STGeomFromText('POINT(0 0)', 0) FROM dbo.Items AS T GROUP BY T.Id, [MixedName];";
        if (key.StartsWith("routine.returns", StringComparison.Ordinal)) return "CREATE FUNCTION dbo.GetItems (@id int) RETURNS @items TABLE (Id int, Name nvarchar(50)) AS BEGIN INSERT INTO @items SELECT Id, Name FROM dbo.Items WHERE Id = @id; RETURN; END;";
        if (key.StartsWith("routine.parameters", StringComparison.Ordinal)) return "CREATE FUNCTION dbo.GetValue (@id int, @name nvarchar(50)) RETURNS int AS BEGIN RETURN @id; END;";
        if (key.StartsWith("insert.source", StringComparison.Ordinal)) return "INSERT INTO dbo.Items (Id, Name) SELECT Id, Name FROM dbo.Source WHERE Id > 0;";
        if (key.Contains(".cte")) return "WITH items (Id, Name) AS (SELECT Id, Name FROM dbo.Items) SELECT Id, Name FROM items;";
        if (key.Contains(".compute")) return "SELECT Amount FROM dbo.Items ORDER BY Amount COMPUTE SUM(Amount) BY Amount;";
        if (key.StartsWith("select.for", StringComparison.Ordinal)) return "SELECT Id, Name FROM dbo.Items FOR XML PATH('item'), ROOT('items');";
        if (key.StartsWith("select.option", StringComparison.Ordinal)) return "SELECT Id, Name FROM dbo.Items WHERE Id > 0 OPTION (RECOMPILE, MAXDOP 1);";
        return field.Category switch
        {
            "insert" => "INSERT TOP (10) INTO dbo.Items (Id, Name) OUTPUT inserted.Id, inserted.Name VALUES (1, N'First'), (2, N'Second');",
            "update" => "UPDATE TOP (10) t SET Name = N'Updated', Amount = 2 OUTPUT inserted.Id, deleted.Name FROM dbo.Items AS t WHERE t.Id = 1 AND t.Amount > 0 OPTION (RECOMPILE);",
            "delete" => "DELETE TOP (10) t OUTPUT deleted.Id, deleted.Name FROM dbo.Items AS t WHERE t.Id = 1 AND t.Amount > 0 OPTION (RECOMPILE);",
            "merge" => "MERGE TOP (10) dbo.Items AS t USING dbo.Source AS s ON t.Id = s.Id WHEN MATCHED AND s.Id > 0 THEN UPDATE SET Name = s.Name, Amount = s.Amount WHEN NOT MATCHED THEN INSERT (Id, Name) VALUES (s.Id, s.Name) WHEN NOT MATCHED BY SOURCE THEN DELETE OUTPUT inserted.Id, deleted.Name OPTION (RECOMPILE);",
            "declare" => key.Contains("cursor") ? "DECLARE items CURSOR LOCAL STATIC FOR SELECT Id, Name FROM dbo.Items WHERE Id > 0;" : "DECLARE @id int = 1, @name nvarchar(50) = N'Example'; DECLARE @items TABLE (Id int, Name nvarchar(50));",
            "code" => key.Contains("transaction") ? "BEGIN TRAN; SET @id = 1; PRINT @id; COMMIT TRAN;" : "IF @id = 1 AND @name IS NOT NULL BEGIN WHILE @id > 0 BEGIN SET @id = @id - 1; END; END ELSE BEGIN TRY PRINT N'Example'; END TRY BEGIN CATCH THROW; END CATCH",
            "routine" => "CREATE PROCEDURE dbo.GetItems @id int, @name nvarchar(50) WITH RECOMPILE AS BEGIN SELECT Id, Name FROM dbo.Items WHERE Id = @id; END;",
            "view" => "CREATE VIEW dbo.ItemView (Id, Name) WITH SCHEMABINDING AS SELECT Id, Name FROM dbo.Items;",
            "createTable" => "CREATE TABLE dbo.Items (Id int NOT NULL, Name nvarchar(50), Amount decimal(10, 2), CONSTRAINT PK_Items PRIMARY KEY (Id)) ON [PRIMARY];",
            "trigger" => "CREATE TRIGGER dbo.ItemsChanged ON dbo.Items WITH EXECUTE AS OWNER AFTER INSERT, UPDATE AS BEGIN SELECT Id, Name FROM inserted; END;",
            "execute" => "EXEC dbo.GetItems @id = 1, @name = N'Example';",
            "labels" => "retry: SELECT Id, Name FROM dbo.Items; GOTO retry;",
            "subquery" => "SELECT d.Id, d.Name FROM (SELECT Id, Name FROM dbo.Items WHERE Id IN (SELECT Id FROM dbo.Source)) AS d WHERE EXISTS (SELECT 1 FROM dbo.Source AS s WHERE s.Id = d.Id);",
            "case" => "SELECT CASE WHEN Id = 1 AND Amount > 0 THEN N'First' WHEN Id = 2 THEN N'Second' ELSE N'Other' END AS ItemName FROM dbo.Items;",
            "setOperator" => "SELECT Id, Name FROM dbo.Items UNION ALL SELECT Id, Name FROM dbo.Source EXCEPT SELECT Id, Name FROM dbo.Archive;",
            "misc" => "SELECT 1;\nGO\nSELECT 2;",
            _ => "SELECT t.Id, t.Name AS ItemName, SUM(s.Amount) AS Total FROM dbo.Items AS t INNER JOIN dbo.Source AS s ON t.Id = s.Id AND s.Amount > 0 WHERE t.Id > 0 AND (t.Name IS NOT NULL OR t.Amount = 1) GROUP BY t.Id, t.Name HAVING SUM(s.Amount) > 0 ORDER BY t.Id, t.Name;"
        };
    }

    // Every catalog segment and enum value is intentionally named; a new rule requires a label and tests.
    private const string Vocabulary = @"
navigation.global|Общие|Global
navigation.queries|Запросы|Queries
navigation.data|Изменение данных|Data changes
navigation.schema|Схема и код|Schema and code
navigation.basics|Основные|Basics
navigation.lists|Списки и скобки|Lists and parentheses
navigation.casingSpacing|Регистр и пробелы|Casing and spacing
navigation.select|SELECT|SELECT
navigation.joinsConditions|JOIN и условия|JOIN and conditions
navigation.subqueriesUnion|Подзапросы и UNION|Subqueries and UNION
navigation.insert|INSERT|INSERT
navigation.updateDelete|UPDATE и DELETE|UPDATE and DELETE
navigation.merge|MERGE|MERGE
navigation.tablesViews|Таблицы и VIEW|Tables and VIEW
navigation.routinesTriggers|Процедуры и триггеры|Routines and triggers
navigation.blocksVariables|Блоки и переменные|Blocks and variables
layout|Общее расположение профиля|Shared profile layout
globalVariable|Регистр системных переменных|Global variable casing
dmlCompact|Короткие операторы DML|Short DML statements
ddlCompact|Короткие объявления и CREATE TABLE|Short declarations and CREATE TABLE
parenthesesCompact|Короткое содержимое скобок|Short parenthesis contents
caseCompact|Короткие выражения CASE|Short CASE expressions
subqueryCompact|Короткие подзапросы|Short subqueries
listFirstItem|Перенос первого элемента списка|First list item wrapping
functionArguments|Перенос аргументов функции|Function argument wrapping
functionParameters|Перенос параметров CREATE FUNCTION|CREATE FUNCTION parameter wrapping
inValues|Перенос значений IN|IN value wrapping
parenthesesStyle|Расположение скобок|Parenthesis layout
blankLinesBetweenStatements|Пустые строки между операторами|Blank lines between statements
blankLinesAfterBatch|Пустые строки после GO|Blank lines after GO
alignDeclarationValues|Выравнивать типы и значения объявлений|Align declaration types and values
alignDdlTypes|Выравнивать типы колонок|Align column data types
alignDdlConstraints|Выравнивать ограничения после типа колонки|Align constraints after column data types
alignListComments|Выравнивать комментарии списков|Align list comments
alignCommentGroups|Выравнивать группы однострочных комментариев|Align single line comment groups
setValueOnNewLineIfLong|Переносить длинное значение SET|Wrap long SET values
newLineAfterTop|Перенос после DISTINCT и TOP|Wrap after DISTINCT and TOP
restoreMoveOnNewLine|Перенос перед MOVE в RESTORE|Wrap before RESTORE MOVE
restoreToOnNewLine|Перенос перед TO в RESTORE MOVE|Wrap before RESTORE MOVE TO
respectFormattingDirectives|Сохранять области SQL Prompt formatting off/on|Preserve SQL Prompt formatting off/on regions
comparisonOperators|Пробелы вокруг операторов сравнения|Comparison operator spacing
beforeTypeParameters|Пробел перед параметрами типа|Space before data type parameters
beforeSemicolon|Пробел перед точкой с запятой|Space before semicolon
multiple|Если несколько элементов|If multiple items
ifLong|Если превышена ширина строки|If longer than line width
relativeSpaces|Отступ в пробелах от начала строки|Offset in spaces from line indentation
absoluteSpaces|Отступ в пробелах от края|Offset in spaces from margin
expandedToStatement|Развёрнуто от начала оператора|Expanded from statement
compact|В одной строке|Single line
general|Общие настройки|General settings
indent|Отступы|Indentation
keywords|Ключевые слова|Keywords
alignment|Выравнивание|Alignment
clauses|Предложения запроса|Query clauses
joins|Соединения таблиц|Table joins
where|Условия WHERE|WHERE conditions
textCase|Регистр текста|Text casing
spacing|Пробелы|Spacing
stackedList|Вертикальные списки|Vertical lists
misc|Пакеты SQL|SQL batches
select|Запрос SELECT|SELECT query
subquery|Подзапросы|Subqueries
setOperator|Объединение запросов|Set operations
case|Выражение CASE|CASE expression
insert|Добавление INSERT|INSERT
update|Изменение UPDATE|UPDATE
delete|Удаление DELETE|DELETE
merge|Слияние MERGE|MERGE
declare|Объявления DECLARE|DECLARE statements
code|Блоки и управление потоком|Blocks and control flow
routine|Процедуры и функции|Procedures and functions
view|Представления VIEW|Views
createTable|Создание таблицы|Create table
trigger|Триггеры|Triggers
execute|Вызов процедуры EXEC|Procedure calls
labels|Метки переходов|Jump labels
maxLineLength|Максимальная длина строки|Maximum line width
lineEnding|Переводы строк|Line endings
finalNewLine|Перевод строки в конце файла|Final newline
style|Стиль отступа|Indent style
size|Размер отступа|Indent size
columns|Колонки|Columns
groupByLayout|Список GROUP BY|GROUP BY list
orderByLayout|Список ORDER BY|ORDER BY list
clauseNewLine|Соединение на новой строке|Join on a new line
conditionNewLine|Условие на новой строке|Condition on a new line
booleanOperatorNewLine|AND и OR на новой строке|AND and OR on a new line
selectAliases|Выравнивать псевдонимы колонок|Align column aliases
setAssignments|Выравнивать присваивания SET|Align SET assignments
declareTypes|Выравнивать типы переменных|Align variable types
keyword|Ключевые слова|Keywords
builtin|Встроенные функции|Built-in functions
dataType|Типы данных|Data types
identifier|Имена объектов|Object names
variable|Имена переменных|Variable names
alias|Псевдонимы|Aliases
formatQuotedIdentifier|Менять регистр имён в кавычках|Change quoted identifier casing
arithmeticOperators|Вокруг арифметических операторов|Around arithmetic operators
beforeComma|Перед запятой|Before comma
afterComma|После запятой|After comma
beforeDot|Перед точкой|Before dot
afterDot|После точки|After dot
beforeScopeResolution|Перед разделителем ::|Before :: separator
afterScopeResolution|После разделителя ::|After :: separator
beforeFunctionArguments|Перед скобкой вызова функции|Before function argument bracket
withinEmptyFunctionArguments|В пустых скобках функции|Inside empty function brackets
withinFunctionArguments|В скобках аргументов функции|Inside function argument brackets
commaPlacement|Положение запятой|Comma placement
spaceAfterLeadingComma|Пробел после ведущей запятой|Space after leading comma
packageDelimiterBlankLine|Пустая строка у GO|Blank line around GO
packageDelimiterBlankLineMode|Положение пустой строки у GO|Blank line placement around GO
singleLine|Компактность одной строки|Single-line layout
maxWords|Максимальное число слов|Maximum word count
maxCharacters|Максимальное число символов|Maximum character count
whenFitsMargin|Одна строка, если помещается в поле|Single line when it fits
list|Список выражений|Expression list
breakBeforeFirstColumn|Перенос перед первой колонкой|Break before first column
stackColumns|Размещать колонки вертикально|Stack columns vertically
stackMode|Способ размещения элементов|Item layout mode
stackList|Размещать список вертикально|Stack list vertically
stackRows|Размещать строки VALUES вертикально|Stack VALUES rows vertically
stackRowsMode|Способ размещения строк VALUES|VALUES row layout mode
from|Источники FROM|FROM sources
into|Назначение INTO|INTO destination
join|Соединения JOIN|JOIN clauses
on|Условие ON|ON condition
having|Условия HAVING|HAVING conditions
groupBy|Группировка GROUP BY|GROUP BY grouping
orderBy|Сортировка ORDER BY|ORDER BY sorting
cte|Общие табличные выражения CTE|Common table expressions
cteQueries|Запросы CTE|CTE queries
for|Вывод FOR XML|FOR XML output
compute|Итоги COMPUTE (SQL 2008)|COMPUTE totals (SQL 2008)
option|Подсказки OPTION|OPTION hints
hints|Подсказки таблицы|Table hints
top|Ограничение TOP|TOP limit
output|Возвращаемые данные OUTPUT|OUTPUT values
values|Значения VALUES|VALUES data
set|Присваивания SET|SET assignments
target|Целевой объект|Target object
source|Источник данных|Data source
using|Источник USING|USING source
when|Ветви WHEN|WHEN branches
then|Действия THEN|THEN actions
parameters|Параметры|Parameters
variables|Переменные|Variables
cursor|Курсор|Cursor
returns|Возвращаемое значение RETURNS|RETURNS value
with|Параметры WITH|WITH options
body|Тело оператора|Statement body
query|Запрос|Query
storage|Параметры хранения|Storage options
events|События|Events
block|Блок BEGIN / END|BEGIN / END block
transaction|Транзакция|Transaction
if|Условие IF / ELSE|IF / ELSE condition
while|Цикл WHILE|WHILE loop
allAnySomeExists|Контекст ALL / ANY / SOME / EXISTS|ALL / ANY / SOME / EXISTS context
inOperator|Контекст IN|IN context
other|Остальные контексты|Other contexts
useSelectFormatting|Наследовать настройки SELECT|Inherit SELECT settings
useStatementFormatting|Наследовать настройки оператора|Inherit statement settings
separateStatements|Разделять операторы переносом|Separate statements with a newline
blankLinesAround|Пустые строки вокруг|Blank lines around
keywordIndent|Отступ ключевого слова|Keyword indentation
listIndent|Отступ списка|List indentation
tableIndent|Отступ имени таблицы|Table name indentation
onKeywordIndent|Отступ ON|ON indentation
onConditionIndent|Отступ условия ON|ON condition indentation
nestedConditionIndent|Отступ вложенных условий|Nested condition indentation
conditionIndent|Отступ условия|Condition indentation
expressionIndent|Отступ выражения|Expression indentation
bodyIndent|Отступ тела|Body indentation
queryIndent|Отступ запроса|Query indentation
braceIndent|Отступ скобок|Bracket indentation
subqueryBraceIndent|Отступ скобок подзапроса|Subquery bracket indentation
columnBraceIndent|Отступ скобок колонок|Column bracket indentation
columnListIndent|Отступ списка колонок|Column list indentation
inputIndent|Отступ входного выражения CASE|CASE input indentation
caseIndent|Отступ CASE|CASE indentation
specIndent|Отступ спецификации|Specification indentation
branchIndent|Отступ правой ветви|Right branch indentation
actionIndent|Отступ действия|Action indentation
codeIndent|Отступ кода|Code indentation
targetIndent|Отступ целевого объекта|Target indentation
fromKeywordIndent|Отступ FROM|FROM indentation
forIndent|Отступ FOR|FOR indentation
asIndent|Отступ AS|AS indentation
percentIndent|Отступ PERCENT|PERCENT indentation
hintsIndent|Отступ подсказок|Hint indentation
whenKeywordIndent|Отступ WHEN|WHEN indentation
whenExpressionIndent|Отступ условия WHEN|WHEN condition indentation
thenKeywordIndent|Отступ THEN|THEN indentation
fromList|Список источников FROM|FROM source list
breakBefore|Перенос перед предложением|Break before clause
breakAfter|Перенос после предложения|Break after clause
onBreakBefore|Перенос перед ON|Break before ON
onBreakAfter|Перенос после ON|Break after ON
wrapCondition|Перенос логических условий|Wrap logical conditions
wrapBeforeOperator|Перенос перед AND / OR|Break before AND / OR
wrapAfterOperator|Перенос после AND / OR|Break after AND / OR
breakAfterXml|Перенос после XML|Break after XML
breakBeforeQuery|Перенос перед запросом|Break before query
breakBeforeTable|Перенос перед таблицей|Break before table
breakBeforeInput|Перенос перед входным выражением|Break before input expression
breakBeforeCase|Перенос перед CASE|Break before CASE
breakBeforeWhenElse|Перенос перед WHEN / ELSE|Break before WHEN / ELSE
breakBeforeThen|Перенос перед THEN|Break before THEN
breakAfterThenElse|Перенос после THEN / ELSE|Break after THEN / ELSE
breakBeforeEnd|Перенос перед END|Break before END
breakBeforeKeyword|Перенос перед ключевым словом|Break before keyword
breakAfterKeyword|Перенос после ключевого слова|Break after keyword
breakBeforeOpen|Перенос перед открывающей скобкой|Break before opening bracket
breakAfterOpen|Перенос после открывающей скобки|Break after opening bracket
breakBeforeClose|Перенос перед закрывающей скобкой|Break before closing bracket
breakAfterClose|Перенос после закрывающей скобки|Break after closing bracket
breakBeforeFrom|Перенос перед FROM|Break before FROM
breakBeforeColumnOpen|Перенос перед скобкой колонок|Break before column opening bracket
breakAfterColumnOpen|Перенос после скобки колонок|Break after column opening bracket
breakBeforeColumnClose|Перенос перед закрывающей скобкой колонок|Break before column closing bracket
breakBeforePercent|Перенос перед PERCENT|Break before PERCENT
breakBeforeFor|Перенос перед FOR|Break before FOR
breakAfterWith|Перенос после WITH|Break after WITH
breakAfterBegin|Перенос после BEGIN|Break after BEGIN
breakBeforeCatch|Перенос перед CATCH|Break before CATCH
breakAfterCondition|Перенос после условия|Break after condition
breakBeforeElse|Перенос перед ELSE|Break before ELSE
breakAfterElse|Перенос после ELSE|Break after ELSE
breakBeforeAs|Перенос перед AS|Break before AS
breakAfterAs|Перенос после AS|Break after AS
spaceAfterKeyword|Пробел после ключевого слова|Space after keyword
spaceBeforeOpen|Пробел перед открывающей скобкой|Space before opening bracket
spaceWithinEmpty|Пробел в пустых скобках|Space inside empty brackets
spaceWithin|Пробел внутри скобок|Space inside brackets
enabled|Использовать отдельное правило|Use a separate rule
offset|Смещение отступа|Indent offset
onNewLineOnly|Только на новой строке|Only on a new line
transparent|Сохранять вложенные отступы|Retain nested indentation
value|Порог|Threshold
inherit|Как в общих настройках|Inherit defaults
always|Всегда|Always
never|Никогда|Never
choice.on|Включено|Enabled
off|Выключено|Disabled
onePerLine|Один элемент на строку|One item per line
auto|Автоматически по ширине|Automatically fit width
leading|В начале строки|At line start
trailing|В конце строки|At line end
choice.insert|Добавлять пробел|Insert space
remove|Убирать пробел|Remove space
upper|ПРОПИСНЫЕ|UPPERCASE
lower|строчные|lowercase
preserve|Не менять регистр|Preserve casing
lf|LF (Unix)|LF (Unix)
crlf|CRLF (Windows)|CRLF (Windows)
cr|CR (классический Mac)|CR (classic Mac)
spaces|Пробелы|Spaces
tabs|Табуляция|Tabs
relative|Относительно родительского блока|Relative to parent block
absolute|От начала строки|From line start
anchor|Относительно опорного токена|Relative to anchor token
before|Перед разделителем|Before delimiter
after|После разделителя|After delimiter
both|До и после разделителя|Before and after delimiter
and|Только AND|AND only
or|Только OR|OR only
none|Без переносов|No wrapping
any|AND и OR|AND and OR
";
}
