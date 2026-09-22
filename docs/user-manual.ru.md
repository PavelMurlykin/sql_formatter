# T-SQL Formatter — руководство пользователя

## Текущее состояние

Это ранний прототип. Сейчас доступен программный разбор T-SQL через библиотеку `TSqlFormatter.Core`. Форматирование SQL, загрузка конфигурации и команды CLI ещё не реализованы. Готового пакета для установки нет.

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

## Ограничения

- CLI пока является заготовкой: он не форматирует SQL и не предоставляет пользовательских команд.
- Парсер не меняет SQL и не создаёт отформатированный текст.
- `LineMap` и вспомогательная навигация по токенам запланированы на следующие этапы.
