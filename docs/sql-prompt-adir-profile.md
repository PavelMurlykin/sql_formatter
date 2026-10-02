# ADIR_SQL_Main: профиль SQL Prompt

Готовый файл: [`examples/profiles/ADIR_SQL_Main.json`](../examples/profiles/ADIR_SQL_Main.json).
Это конфигурация нашего расширения в формате JSON v2, подготовленная по двум экспортам
ADIR_SQL_Main и всем 16 скриншотам из `.codex-remote-attachments/sql_prompt`.
Импортировать нужно этот JSON; непосредственный импорт `.sqlpromptstylev2` или исходного
JSON SQL Prompt не добавлялся.

Установите собранный VSIX для своей IDE. Откройте **SQL Formatter → Профили → Импорт JSON…**,
выберите файл, затем **Сохранить профиль** с именем `ADIR_SQL_Main`. При необходимости
нажмите **Использовать по умолчанию** и подтвердите **OK**. Ближайшая конфигурация проекта
`.tsqlformatter.json` имеет приоритет: её нужно согласовать с выбранным стилем.
Для проекта этот же JSON можно сохранить как `.tsqlformatter.json`.

Профиль использует CRLF и завершающий перевод строки для файлов Windows. Эти два параметра
выбраны для проекта; они не являются полями экспортированного стиля SQL Prompt.

## Соответствие исходным настройкам

| Скриншот / группа | Перенесённое поведение | Настройки расширения |
| --- | --- | --- |
| 1. Whitespace | 4 пробела, ширина 160, одна пустая строка между операторами и после GO, отсутствие пробела перед `;` | `indent`, `general`, `layout.blankLinesBetweenStatements`, `layout.blankLinesAfterBatch`, `spacing.beforeSemicolon` |
| 2. Lists | Первый элемент на новой строке при нескольких элементах; ведущие запятые с пробелом; отступ списка; выравнивание комментариев | `layout.listFirstItem`, `layout.listIndent`, `stackedList.*`, `layout.alignListComments` |
| 3. Parentheses | Скобки от начала оператора, отступ содержимого, сворачивание короткого содержимого до 120 | `layout.parenthesesStyle`, `layout.parenthesesCompact` |
| 4. Casing | Верхний регистр ключевых слов, встроенных функций/типов, системных переменных | `textCase.keyword/builtin/dataType/globalVariable`; локальные переменные, объекты и псевдонимы сохраняются |
| 5. DML | Сворачивание DML до 100 и подзапросов до 55; перенос после DISTINCT/TOP; FROM/WHERE рядом с первым выражением; вертикальные GROUP/ORDER при нескольких выражениях | `layout.dmlCompact`, `layout.subqueryCompact`, `layout.newLineAfterTop`, `layout.listFirstItem` |
| 6. DDL | Параметры процедуры с новой строки, выравнивание типов/значений, короткие объявления и CREATE TABLE до 55 | `routine.parameters.*`, `layout.alignDdlTypes`, `layout.alignDdlConstraints`, `layout.alignDeclarationValues`, `layout.ddlCompact`; параметры функций — отдельно `layout.functionParameters` |
| 7. Control flow | BEGIN/END на отдельных строках, отступ содержимого, без сворачивания IF/WHILE/TRY | `code.*`, общие правила `layout.listFirstItem` для хранимого кода |
| 8. CTE | Имя рядом с WITH, AS рядом с именем; списки колонок и тело со скобками/отступами; короткое содержимое сворачивается | `select.cte.*`, `layout.parenthesesStyle`, `layout.parenthesesCompact` |
| 9. Variables | Выравнивание типов и значений DECLARE; отсутствие пробела перед точностью типа; перенос длинного SET после `=` | `layout.alignDeclarationValues`, `spacing.beforeTypeParameters`, `layout.setValueOnNewLineIfLong` |
| 10. JOIN | JOIN от начала оператора, ON с отступом 4, условие рядом с ON | Общая политика `layout.listFirstItem`, `joins` |
| 11. INSERT | Скобки списков колонок/значений от начала INSERT; короткие списки сворачиваются до 120; строки VALUES вертикально | `insert.values.*`, `layout.parenthesesStyle`, `layout.parenthesesCompact` |
| 12. Restore | MOVE и TO на новых строках, TO с дополнительным отступом | `layout.restoreMoveOnNewLine`, `layout.restoreToOnNewLine` |
| 13. Functions | Аргументы переносятся при превышении 160; без внутренних пробелов в скобках | `layout.functionArguments`, `spacing.beforeFunctionArguments`, `spacing.withinFunctionArguments`, `spacing.withinEmptyFunctionArguments` |
| 14. CASE | WHEN/ELSE с отступом от CASE, результат с дополнительным отступом, END к CASE; короткий CASE до 78 | `case.*`, `layout.caseCompact` |
| 15. IN | Короткие списки в строке, первый элемент переносится при превышении ширины; без пробелов внутри скобок | `layout.inValues` |
| 16. Operators | AND/OR перед условием с отступом; BETWEEN остаётся выражением; пробелы вокруг арифметических операторов и сравнений | Общая политика `layout.listFirstItem`, `spacing.arithmeticOperators`, `spacing.comparisonOperators` |

Новые `layout.*` правила отключены по умолчанию. `layout.listFirstItem`, когда задан явно,
включает общую политику списков и вложенного хранимого кода: предложения запроса от начала
оператора, ON с одним шагом отступа, AND/OR с шагом от WHERE/HAVING/ON, BEGIN/END и отступы тела. Эта политика
работает по промежуткам между токенами AST; обычные профили без неё используют прежние
структурные правила. Все параметры доступны в редакторе настроек на русском и английском.

## Уточнение по функциям, таблицам и представлениям

Текущий JSON требует Visual Studio 0.2.7 / SSMS 0.6.6; ранее сохранённый профиль нужно
импортировать заново. `layout.functionParameters=ifLong` выведен из примеров функций:
общий параметр SQL Prompt `placeFirstProcedureParameterOnNewLine=always` относится к
процедурам и не должен принудительно разбивать короткие заголовки функций. Проверяется
длина всего заголовка до закрывающей скобки; комментарии в параметрах оставляют список
развёрнутым. `inherit` оставляет `routine.parameters`; остальные режимы — `always`,
`never`, `multiple`, `ifLong`. Это уточнение на основе корпуса, а не отдельное поле экспорта.

`layout.alignDdlConstraints=true` дополняет `alignDataTypesAndConstraints`: выравнивается
первый модификатор после типа колонки, включая NULL/NOT NULL/IDENTITY. Выравнивание типов
управляется отдельно через `layout.alignDdlTypes`. У новых настроек defaults — `inherit`
и `false`; они доступны в обоих языках редактора. `routine.returns.breakBefore=always`
и `view.query.*` явно задают перенос RETURNS, AS и отступ запроса представления.
Исправлены парные скобки таблиц с PERIOD/inline INDEX и псевдонимов производных таблиц.
[Сверка пяти папок и повторная проверка процедур](sql-prompt-adir-additional-validation.md).

## Границы соответствия

- `UseObjectDefinitionCase` требует каталога определений объектов/подключения к БД. У расширения
  такого источника нет: имена объектов, схем, пользовательских типов и локальных переменных
  сохраняются. Это не восстановление регистра из базы данных.
- Автоматическое выравнивание распространённых узоров внутри многострочных комментариев
  не реализовано. Текст комментариев сохраняется, группы однострочных комментариев
  выравниваются отдельной настройкой.
- Сложные ограничения CREATE TABLE, перенос произвольных DDL (например GRANT/ALTER
  AUTHORIZATION), длинных IN со смешанными вложенными выражениями и некоторых сложных
  конструкций может отличаться от SQL Prompt. Ширина 160 — ориентир, а не обязательное
  обрезание любой строки.
- Ошибочный SQL обрабатывается существующим безопасным режимом. Файлы с многострочными
  строковыми литералами/идентификаторами сохраняются целиком; они учитываются в сверке
  отдельной категорией и не считаются доказательством совпадения форматтеров.
- `layout.respectFormattingDirectives=true` сохраняет исходный текст между комментариями
  `-- SQL Prompt formatting off` и `-- SQL Prompt formatting on`; незакрытая область
  сохраняется до конца файла. SQL внутри строк не форматируется.
- Экспортированные настройки подтверждают стиль, но не гарантируют, что каждый пример
  процедуры не содержит ручных правок или форматирования другой версией SQL Prompt.

## Воспроизводимая сверка

Сверка читает только существующие `.sql`, которые затронуты коммитами Git за 2026 год
по часовому поясу Москвы. SQL не исполняется, исходные файлы не перезаписываются.
Проверяются повторный разбор, неизменность токенов (кроме регистра), точное сохранение
литералов/комментариев и повторное форматирование. Точное совпадение сравнивается без
различий CRLF/LF и завершающих переводов строки; остальные пробелы значимы.

```powershell
dotnet run --project benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -- --sql-prompt-validation "C:\git_alfa\hell\Hell\Stored Procedures" "C:\git\sql_formatter\examples\profiles\ADIR_SQL_Main.json" "C:\git\sql_formatter\artifacts\sql-prompt\comparison" git
```

Инструмент записывает `files.tsv`, `summary.txt` и локальные результаты для просмотра
различий. Последний необязательный аргумент ограничивает число файлов. Режим `last-write`
вместо `git` отбирает по времени изменения на диске и не использовался для итоговой сверки.
Локальные SQL и их результаты находятся в игнорируемой папке `artifacts`; рабочие процедуры
не добавляются в тесты или документацию проекта. Итоговые числа и ограничения записаны
в [отчёте](sql-prompt-adir-validation.md).
