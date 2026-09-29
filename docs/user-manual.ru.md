# T-SQL Formatter — руководство пользователя

## Текущее состояние

Это ранний прототип. Через `TSqlFormatter.Core` доступны разбор T-SQL, навигация по токенам, классификация комментариев, перевод между смещениями и позициями строк, построение и рендеринг layout-документа, изменение регистра ключевых слов и форматирование поддержанных форм `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE`, включая CTE, подзапросы, `CASE`, оконные функции, `FROM`, `JOIN` и `APPLY`, а также простые формы управляющих конструкций и хранимого кода. Через `TSqlFormatter.Configuration` доступны настройки JSON, именованные профили и ограниченный набор свойств `.editorconfig`. CLI форматирует SQL из stdin или одного файла в stdout; для нескольких файлов и каталогов доступны `--write` и `--check` с автоматическим поиском конфигурации для каждого файла. CLI можно собрать и установить как локальный предварительный `dotnet tool`; стабильный пакет не опубликован. Экспериментальный VSIX форматирует весь открытый `.sql`-документ, оператор по выделению или ближайший к каретке оператор и загружает конфигурацию для файла. Отдельный VSIX для SSMS 22 экспериментально форматирует активный `.sql`-документ.

### Управляющие конструкции и хранимый код

CLI и команды редактора используют один движок. Для простых операторов он расставляет переносы/отступы у `DECLARE` (несколько переменных), `SET`, `IF`/`ELSE`, `BEGIN`/`END`, `WHILE`, `BEGIN TRY`/`BEGIN CATCH`, `THROW`, а также у тела `CREATE`/`ALTER PROCEDURE`, `FUNCTION` и запроса `VIEW`. Например, `begin set @a=1; set @b=2; end` превращается в `BEGIN` с двумя операторами `SET` на отдельных строках с отступом и заключительным `END`. Форматирование не меняет строковые литералы и не выполняет SQL. Если в управляющем заголовке или между операторами есть комментарии либо форма не поддержана, исходная раскладка сохраняется; регистр распознанных ключевых слов всё же может измениться согласно настройке. Проверьте результат перед сохранением важного скрипта.

### Выравнивание по столбцам

В JSON-конфигурации можно включить `alignment.selectAliases`, `alignment.setAssignments` и `alignment.declareTypes` (по умолчанию все `false`). Первое выравнивает явные `AS` и псевдонимы в простом списке `SELECT`, второе — знаки `=` простых присваиваний в `UPDATE ... SET`, третье — типы нескольких переменных в `DECLARE`. Например, при `"selectAliases": true` выражения `Id AS CustomerId, LongName AS Name` располагаются на отдельных строках с `AS` в одном столбце. Выравнивание отключается для конкретного списка при комментариях, сложной форме, табуляции или если оно превысит `general.maxLineLength`; тогда действует прежняя раскладка. Для одиночного `SET @x = ...` и неявных псевдонимов это выравнивание не применяется. Страницы параметров VS/SSMS пока не содержат этих переключателей; для редакторов используйте `.tsqlformatter.json`. Импорт профиля в VS не переносит эти переключатели на страницы параметров.

## Подготовка

Нужны исходный код проекта и .NET 10 SDK (допускаются новые feature-band версии 10.0). Для сборки всего solution, включая каркас VSIX, дополнительно нужны Windows, Visual Studio 2026 с компонентом Visual Studio extension development и доступ к NuGet. Для работы только с CLI можно собирать проект `src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj` без Visual Studio. Общий Core и Configuration остаются совместимы с `netstandard2.0`, а расширения IDE — с `net472`. Из корня репозитория выполните:

```powershell
dotnet restore TSqlFormatter.sln
dotnet build TSqlFormatter.sln --no-restore
dotnet test TSqlFormatter.sln --no-build --no-restore
```

Чтобы использовать парсер в своём C# проекте, добавьте ссылку на `src/TSqlFormatter.Core/TSqlFormatter.Core.csproj`.

## Локальный dotnet tool и примеры CI

CLI упаковывается как `TSqlFormatter.Tool` версии `0.1.0-preview.6` с командой `tsqlformat`. Нужны .NET 10 SDK для сборки и .NET 10 Runtime для запуска; установленного только .NET 8 Runtime недостаточно. Из корня репозитория соберите пакет и установите его в игнорируемый Git каталог (PowerShell):

```powershell
dotnet pack src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj -c Release -o artifacts/tool
dotnet tool install TSqlFormatter.Tool --tool-path artifacts/tool-bin --source artifacts/tool --version 0.1.0-preview.6
.\artifacts\tool-bin\tsqlformat.exe query.sql --check
```

Инструмент не опубликован в NuGet; команды выше устанавливают только локально собранный пакет, поэтому примеры CI предполагают наличие исходного кода этого проекта. При переходе с предварительной версии `0.1.0-preview.1` установите .NET 10 Runtime и переустановите пакет. `tsqlformat` принимает stdin или один SQL-файл для вывода в stdout, а несколько файлов или каталог — с `--check` либо `--write`. Для каждого файла `--check` возвращает `0` (готово), `1` (нужно форматирование) или `2` (ошибка); правила пакетного кода выхода описаны ниже.

В `examples/ci/` находятся [пример GitHub Actions](../examples/ci/github-actions.yml), [пример Azure Pipelines](../examples/ci/azure-pipelines.yml), [пример pre-commit](../examples/ci/.pre-commit-config.yaml) и вспомогательный скрипт `check_sql_files.py`. Примеры проверки пользовательских SQL не активированы в этом репозитории. Перед использованием замените каталог `database` на свой и скопируйте YAML в соответствующее место проекта. Примеры GitHub Actions и Azure Pipelines сами собирают локальный пакет и запускают `tsqlformat database --check`, без Python. Pre-commit по-прежнему использует Python 3 и скрипт для переданных `.sql`-файлов; установите `tsqlformat` заранее и добавьте его каталог в `PATH`. Скрипт возвращает `0`, если все файлы готовы, `1`, если нужны изменения, и `2` при ошибке или отсутствии инструмента.

В репозитории также действует [workflow GitHub Actions](../.github/workflows/ci.yml) для push/PR в `main` и ручного запуска. Он собирает и тестирует Core/CLI на Linux и Windows, упаковывает, устанавливает и проверяет CLI, а на Windows собирает solution и проверяет пакет Visual Studio VSIX. Workflow не публикует пакет, не запускает расширение внутри IDE и не проверяет пользовательские SQL-файлы. Обязательность проверок для слияния настраивается отдельно в защите ветки GitHub.

## SSMS 22: экспериментальное расширение

Соберите `src/TSqlFormatter.Ssms/TSqlFormatter.Ssms.csproj` в конфигурации Release. Файл `src/TSqlFormatter.Ssms/bin/Release/net472/TSqlFormatter.Ssms.vsix` рассчитан на 64-битный SSMS 22; устанавливайте его в SSMS, а не в Visual Studio. После перезапуска SSMS в меню «Сервис» доступны команды с префиксом `T-SQL Formatter (SSMS):`: Format Document, Format Selection и Format Statement. Откройте `.sql` в редакторе запросов. Format Document форматирует весь буфер общим движком. Format Selection форматирует оператор с выделенным SQL, а Format Statement — оператор рядом с кареткой; остальные операторы не меняются. При ошибке SQL, размере буфера более 16 млн символов или изменении буфера во время обработки правка не применяется. Если цель нельзя безопасно форматировать, показывается диагностический код, а не сообщение об уже отформатированном SQL. Каждая правка отменяется одним Undo и сохраняет выделение или положение каретки; сохранять файл нужно отдельно. Подключение к серверу не требуется.

Настройки по умолчанию находятся в `Сервис → Параметры → T-SQL Formatter (SSMS) → General` (ссылка General открывает классическое окно параметров). Для профиля Default доступны максимальная длина строки, окончания строк, завершающий перевод строки, размер отступа/табуляция и регистр ключевых слов. Профили Compact и Expanded используют собственные настройки вместо этих полей Default. Если найден `.editorconfig`, он накладывает общие настройки пробелов/переводов строк, а ближайший `.tsqlformatter.json` имеет приоритет над ними и профилем SSMS. Практически проверен только SSMS 22.10.1 (x64) с простым SQL; другие версии 22.x и сложные сценарии остаются непроверенными. Матрица и ограничения описаны в [результатах spike](ssms-spike.md). Microsoft официально не поддерживает сторонние расширения SSMS, поэтому этот VSIX предназначен только для экспериментов.

## Visual Studio: экспериментальная команда

На Windows с Visual Studio 2026 и компонентом Visual Studio extension development соберите `src/TSqlFormatter.VisualStudio/TSqlFormatter.VisualStudio.csproj` в конфигурации Release. Полученный `src/TSqlFormatter.VisualStudio/bin/Release/net472/TSqlFormatter.VisualStudio.vsix` предназначен только для проверки интеграции. Установите VSIX, перезапустите Visual Studio, откройте `.sql` и используйте три команды `Tools → Format T-SQL ...`, описанные ниже. Диагностические probe-команды больше не входят в меню. Проверенная версия IDE и оставшиеся пробелы указаны в [матрице ручной проверки](visual-studio-smoke.md).

Перед установкой выполните из корня репозитория `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Vsix.ps1` для проверки манифеста предварительного VSIX и обязательных сборок. Такая проверка пакета не подтверждает работу расширения внутри Visual Studio.

`Tools → Format T-SQL Document` форматирует весь открытый `.sql`, используя общие настройки VSIX как основу, поверх которых накладываются `.editorconfig` и ближайший `.tsqlformatter.json`. Разбор и построение результата выполняются в фоне, после чего VSIX проверяет, что активный буфер не сменился и не изменился, и применяет результат одной транзакцией Undo. При невалидном SQL или конфигурации документ остаётся неизменным и показывается диагностика. Если SQL уже отформатирован, появляется информационное сообщение. Максимальный размер буфера — 16 Ми символов UTF-16. Положение каретки и выделения сохраняется приблизительно по смещениям; после больших изменений раскладки оно может не соответствовать прежнему логическому элементу. Команда доступна и для других файлов в меню, но работает только с `.sql`. Document, Selection, Statement, Undo, сохранение и две открытые SQL-вкладки проверены вручную на простом SQL в Visual Studio 2026; непроверенные случаи перечислены в матрице.

`Tools → Format T-SQL Selection` требует непустое выделение внутри одного оператора T-SQL. Форматируется весь содержащий его оператор, но другие операторы и окружающий текст не изменяются. Если граница выделения проходит посередине токена (включая строку или идентификатор), выделение захватывает несколько операторов либо весь скрипт не разбирается, команда оставляет документ без изменений и показывает диагностику. Результат применяется одной правкой и одним Undo только если буфер всё ещё прежний. Сейчас выбирается оператор верхнего уровня, а не минимальный вложенный запрос; расположение выделения после форматирования приблизительное. Ограничение размера и поиск конфигурации такие же, как у Format Document.

`Tools → Format T-SQL Statement` форматирует только ближайший к каретке оператор верхнего уровня, даже если каретка стоит в пробелах между операторами. Остальной текст не меняется. Пустой скрипт, каретка вне документа, ошибка разбора или невозможность безопасно форматировать отдельный оператор дают диагностику без правки. Форматирование выполняется в фоне, а правка применяется одним Undo только к неизменённому активному буферу. Лимит 16 Ми символов и поиск конфигурации такие же, как у двух других команд. Пока не выбирается минимальный вложенный оператор.

VSIX добавляет страницу `Tools → Options → T-SQL Formatter → General`: максимальная длина строки (1–4096, по умолчанию 100), окончание строки LF/CRLF/CR (LF), завершающая новая строка (выключена), размер отступа (0–32, по умолчанию 4), табуляция (выключена) и регистр ключевых слов (Upper). В Visual Studio 2026 ссылка General на странице Settings открывает классический диалог настроек. При профиле IDE `Default` эти настройки служат основой для всех трёх команд форматирования. Они не влияют на CLI и не записываются в файл проекта. Общие настройки `.editorconfig` накладываются поверх основы IDE; команды затем ищут `.tsqlformatter.json` от каталога открытого SQL-файла вверх до `.git` или корня файловой системы. Указанные в ближайшем JSON поля имеют наивысший приоритет. Без файлов остаётся основа IDE. JSON читается как UTF-8 с лимитом 64 МиБ и 16 Ми символов после декодирования. Ошибки файла (`TSF9000`/`TSF9001`) или настроек (`TSF2000`) показываются без изменения буфера. Переключение Keyword casing в Lower проверено вручную при форматировании Document; затем восстановлено Upper.

`Tools → Options → T-SQL Formatter → SQL Preview` содержит редактируемый пример SQL (до 4096 символов) и результат форматирования. После паузы ввода в 300 мс используется тот же Core-форматтер, что и в командах; при возвращении на страницу предпросмотр обновляется с текущими настройками IDE. Предпросмотр использует только настройки IDE, без конфигурации проекта, и не меняет открытый SQL-документ. При ошибке в примере показывается диагностика вместо результата. Поведение в установленной Visual Studio пока требует ручной проверки.

Страницы `SELECT`, `JOIN` и `WHERE` в `Tools → Options → T-SQL Formatter` задают поддержанную раскладку. На странице SELECT для колонок и элементов GROUP BY/ORDER BY доступны `Auto` и `OnePerLine`. Страница JOIN управляет переносом поддержанных JOIN/APPLY и условий ON (оба параметра включены по умолчанию). Страница WHERE управляет переносом условий WHERE/HAVING и операторов AND/OR (оба включены). При профиле IDE `Default` значения страниц используются командами форматирования и SQL Preview; одноимённые поля конфигурации проекта имеют приоритет в командах. Неподдержанные конструкции SQL сохраняют исходную раскладку. Поведение страниц в работающей IDE ещё требует ручной проверки.

`Tools → Options → T-SQL Formatter → Profile` выбирает `Default`, `Compact` или `Expanded`. `Default` использует значения страниц General/SELECT/JOIN/WHERE; `Compact` задаёт встроенную ширину 120 символов, `Expanded` — ширину 80 и по одному элементу в строке для SELECT, GROUP BY и ORDER BY. При `Compact` или `Expanded` остальные страницы IDE не влияют на форматирование до переключения обратно на `Default`; одноимённые поля проектной конфигурации по-прежнему имеют приоритет для команд. SQL Preview использует выбранный профиль, но не конфигурацию проекта. Выбор профиля в установленной IDE ещё требует ручной проверки.

`Tools → T-SQL Formatter: Export Profile...` после выбора пути сохраняет действующие настройки IDE в JSON версии 1. `Tools → T-SQL Formatter: Import Profile...` принимает JSON версии 1 (до 1 МиБ, строгий UTF-8), переносит значения на страницы настроек IDE, сохраняет их и выбирает `Default`. Открытый SQL-документ и проектная конфигурация не изменяются. При невалидном файле или превышении ограничений IDE (длина строки 4096, отступ 32) действующие настройки IDE остаются неизменными, показывается ошибка. Экспортируемый JSON — полный снимок параметров, который можно вручную использовать как `.tsqlformatter.json`; это не именованный пользовательский профиль. Команды ещё нужно проверить вручную в установленной Visual Studio.

`Tools → Options → T-SQL Formatter → General → Format on save` по умолчанию имеет значение `Off`. `CurrentDocument` форматирует активный документ `.sql` непосредственно перед записью Visual Studio; `OnlyWhenProjectConfigExists` делает это только при наличии `.tsqlformatter.json`, найденного обычным поиском вверх по каталогам. Применяются та же проектная конфигурация, выбранный профиль IDE, строгий разбор, проверка снимка и одна правка Undo, что и в ручной команде Document. Для автоматического форматирования при сохранении действует более строгий лимит буфера 8 Ки символов и двухсекундный токен кооперативной отмены; ручная команда Document сохраняет лимит 16 Ми символов. Ошибка SQL/конфигурации или отмена не препятствуют сохранению: записывается неотформатированный буфер, показывается ошибка. Прототип обрабатывает только активный редактор; неактивные файлы при Save All не форматируются. Хук сохранения проверен сборкой и модульными тестами, но перед включением для ценных файлов его ещё нужно вручную проверить в работающей Visual Studio.

`General → Save exclusions` принимает разделённые точкой с запятой шаблоны без учёта регистра, например `*.generated.sql; generated/**`. Шаблон без косой черты проверяется по имени файла, с чертой — по пути начиная с любого каталога. `*` соответствует символам внутри сегмента пути, `**` — нескольким каталогам, `?` — одному символу. Исключения действуют в обоих режимах сохранения, но не затрагивают ручные команды форматирования. Пустое значение ничего не исключает.

`Tools → T-SQL Formatter: Paste Formatted SQL (Prototype)` читает из буфера обмена до 8 Ки символов SQL, форматирует этот фрагмент с выбранным профилем IDE и проектной конфигурацией, затем заменяет выделение или вставляет текст в позицию каретки активного документа `.sql`. Используется двухсекундный токен кооперативной отмены. Правка образует одну транзакцию Undo; при вставке каретка переходит за вставленный текст. Пустой или невалидный SQL в буфере обмена, изменение буфера редактора, отмена либо ошибка разбора/конфигурации оставляют документ без изменений. Проверяется только вставляемый фрагмент, не весь итоговый документ. Это явная экспериментальная команда: обычный `Ctrl+V` не меняется, автоматического Format on Paste пока нет. Проверяйте прототип в одноразовом файле; работа внутри Visual Studio ещё требует ручной проверки.

Лимиты автоматических действий выбраны по [локальному замеру на синтетическом SQL](performance/editor-workflow-validation.md). Отмена кооперативная: синхронный разбор ScriptDom может превысить номинальные две секунды, прежде чем токен будет проверен. Отзывчивость интерфейса в установленной IDE пока не проверена.

Статус форматирования и ошибки отображаются в строке состояния Visual Studio и в панели вывода **T-SQL Formatter**; при ошибке панель активируется. При наличии кода диагностики он включается в сообщение. Сообщение об успешном форматировании в строке состояния подтверждено в установленной IDE; поведение панели при ошибках там ещё не проверено.

## CLI: stdin, файлы и каталоги

Из корня репозитория после сборки передайте SQL через stdin:

```powershell
'select Id from T' | dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore
```

Результат выводится в stdout; вместо отсутствующего аргумента можно передать `-`. `--help` показывает краткую справку. Диагностики идут в stderr. Код завершения `0` означает успешное форматирование, `2` — ошибку разбора SQL, конфигурации, ввода-вывода или аргументов. Код `1` используется только в режиме `--check`, когда форматирование требуется. При ошибке разбора SQL не выводится. CLI использует `Default`, если `--profile` не выбирает `Default`, `Compact` либо `Expanded` (без учёта регистра); выбор работает для stdin, одного файла и пакетного режима. Других флагов настроек нет.

Чтобы прочитать один файл `query.sql` из текущего каталога:

```powershell
dotnet run --project src/TSqlFormatter.Cli/TSqlFormatter.Cli.csproj --no-restore -- query.sql
```

CLI читает одиночный файл как UTF-8 (с поддержкой BOM), выводит форматированный SQL в stdout и не изменяет исходный файл. При отсутствии файла или ошибке чтения возвращается код `2`, сообщение пишется в stderr, stdout остаётся пустым. Несколько файлов без `--check` или `--write` не принимаются: общий вывод в stdout для них не предусмотрен.

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

### Пакетная проверка и запись

Для каталога или нескольких файлов требуется `--check` либо `--write`; без режима CLI возвращает `2`. Например:

```powershell
tsqlformat ./sql --check
tsqlformat first.sql second.sql --write
tsqlformat ./sql --check --exclude generated --exclude reports/legacy.sql
```

Каталог обходится рекурсивно: выбираются файлы с расширением `.sql` без учёта регистра, включая скрытые; ссылки на каталоги и файлы не обходятся. Явно указанный файл принимается с любым расширением. Повторяющиеся и перекрывающиеся пути обрабатываются один раз, в отсортированном порядке. `--exclude` можно повторять только при наличии каталога: это буквальный относительный путь от каждого указанного каталога до файла или подкаталога. Исключается сам путь и всё внутри подкаталога; glob-шаблоны, абсолютные пути и `..` не поддерживаются. На Windows сравнение путей нечувствительно к регистру, на других системах — чувствительно. Пустой каталог без подходящих файлов даёт `TSF9000` и итоговый код `2`.

В пакетном `--check` stdout пуст, а пути файлов, требующих форматирования, выводятся в stderr как `Would reformat: путь`. Итоговый код: `2`, если хотя бы один путь/SQL/конфигурация дал ошибку; иначе `1`, если хотя бы один файл требует форматирования; иначе `0`. Пакетный `--write` оставляет stdout пустым и обрабатывает файлы независимо: ошибка одного файла не препятствует записи успешных файлов, но сам ошибочный файл остаётся неизменным. Итоговый код `2` при любой ошибке, иначе `0`; общей транзакции на все файлы нет. Для каждого файла применяется собственная найденная конфигурация. Одиночный файл без режима по-прежнему выводится в stdout.

### Поиск конфигурации для файла

Для `query.sql`, в том числе с `--write` или `--check`, CLI начинает с выбранного встроенного профиля (`Default`, если выбор не указан), накладывает свойства `.editorconfig` из каталога SQL-файла и родителей до `root = true` либо корня файловой системы, затем поля ближайшего `.tsqlformatter.json`. Более близкий `.editorconfig` и более поздняя подходящая секция имеют приоритет; поиск JSON останавливается после каталога с `.git` либо у корня файловой системы. Поля JSON имеют приоритет над `.editorconfig` и профилем. Путь рассматривается как абсолютный после разрешения относительно текущего каталога; символические ссылки при обходе родителей не раскрываются. Для stdin действует только выбранный профиль, поиск файлов не выполняется.

Например, `tsqlformat query.sql --profile Expanded` выводит элементы SELECT по одному в строке, если JSON не переопределяет `select.columns`. `tsqlformat ./sql --check --profile Compact` применяет Compact к каждому выбранному файлу до конфигурации именно этого файла. Неизвестный ID профиля даёт `TSF2000`; отсутствующее или повторное значение `--profile` даёт `TSF9000`, код `2`, без SQL в stdout. CLI, Visual Studio и SSMS используют один resolver: их исходные настройки отличаются только при заданных параметрах IDE. При одинаковом SQL и итоговых настройках все адаптеры вызывают один Core-форматтер.

Ограниченный набор `.editorconfig`: секции `[*]` и `[*.sql]`; свойства `indent_style` (`space`/`tab`), числовое `indent_size` от 0 до 32, `end_of_line` (`lf`/`crlf`/`cr`) и `insert_final_newline` (`true`/`false`). `unset` снимает действие унаследованного свойства. Другие секции, свойства и недопустимые значения игнорируются; сложные glob-шаблоны, `tab_width`, `charset` и `trim_trailing_whitespace` не поддерживаются. Файл `.editorconfig` читается как UTF-8, до 1 МиБ; ошибка чтения или кодировки даёт `TSF9000`, превышение размера — `TSF9001`.

Файл читается как UTF-8, проверяется по схеме версии 1, и найденные настройки накладываются на выбранный профиль и `.editorconfig`. При невалидной конфигурации CLI выводит `TSF2000` и путь к файлу в stderr, возвращает `2`, не печатает SQL и не выполняет `--write`. При ошибке чтения возвращается `TSF9000`. Если конфигурации нет, действуют профиль и подходящие свойства `.editorconfig`.

### Крупные входные данные

CLI читает SQL и JSON-конфигурацию с ограничением: не более 64 МиБ для каждого файла и не более 16 Ми символов UTF-16 после декодирования (для stdin действует ограничение по символам); для `.editorconfig` действует отдельный лимит 1 МиБ. При превышении возвращается код `2` с `TSF9001` в stderr: SQL не выводится, `--write` не выполняется. Ошибка UTF-8 даёт `TSF9000`. Сам парсер по-прежнему работает с текстом целиком, поэтому произвольно большие файлы пока не поддерживаются.

При Ctrl+C CLI запрашивает отмену, выводит `TSF9002` в stderr и завершается с кодом `130`. При отмене до замены файла `--write` оставляет исходный файл неизменным; временный файл удаляется. Разбор ScriptDom синхронный, поэтому отмена может быть замечена только после завершения текущего вызова парсера.

## Конфигурация JSON

Для чтения и записи настроек добавьте ссылку на `src/TSqlFormatter.Configuration/TSqlFormatter.Configuration.csproj`. Поддерживается файл `.tsqlformatter.json` версии 1 с текущими разделами модели настроек:

```json
{
  "version": 1,
  "general": { "maxLineLength": 100, "lineEnding": "lf", "finalNewLine": false },
  "indent": { "style": "spaces", "size": 4 },
  "keywords": { "case": "upper" },
  "select": { "columns": "auto" },
  "alignment": { "selectAliases": false, "setAssignments": false, "declareTypes": false },
  "joins": { "clauseNewLine": true, "conditionNewLine": true },
  "where": { "conditionNewLine": true, "booleanOperatorNewLine": true },
  "clauses": { "groupByLayout": "auto", "orderByLayout": "auto" }
}
```

`lineEnding` принимает `lf`, `crlf` или `cr`; `indent.style` — `spaces` или `tabs`; `keywords.case` — `upper`, `lower` или `preserve`; раскладки — `auto` или `onePerLine`. Четыре поля переносов JOIN/WHERE имеют логический тип. `general.maxLineLength` должен быть целым числом не меньше 1, `indent.size` — целым числом не меньше 0. Отсутствующие разделы и поля получают встроенные значения по умолчанию (в VSIX — настройки IDE). Сериализатор записывает все поддержанные поля и завершающий LF.

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

Это явное чтение файла приложением. CLI и VSIX ищут конфигурацию автоматически для именованного SQL-файла; `ScriptDomSqlFormatter` сам этого не делает. Другой интеграции доступен `new SqlFormatterConfigurationResolver().ResolveForSqlFile("query.sql")`, возвращающий `Default` или проверенные настройки и диагностики при ошибке. Файл плана показывает также будущие поля (`expressions`, `aliases` и другие); текущий сериализатор отклоняет их как неизвестные.

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

Параметры `FormattingOptionsOverrides` соответствуют поддержанным полям JSON, включая `maxLineLength`, `lineEnding`, `finalNewLine`, `indentSize`, `useTabs`, `keywordCase`, `selectColumns`, `groupByLayout`, `orderByLayout`, `alignSelectAliases`, `alignSetAssignments` и `alignDeclareTypes`. Это API для приложений; CLI загружает найденный файл автоматически и принимает `--profile`, но не принимает флаги отдельных полей.

### Именованные профили

`FormattingProfileCatalog` содержит `Default` (обычные значения), `Compact` (ширина строки 120) и `Expanded` (ширина 80, колонки `SELECT` и элементы `GROUP BY`/`ORDER BY` по одному на строку). Идентификаторы сравниваются без учёта регистра. Выберите профиль через `profileId`:

```csharp
var projectProfile = new FormattingProfile(
    "project", "Project", FormattingOptions.Default.With(indent: new IndentOptions(2)));
var catalog = new FormattingProfileCatalog(new[] { projectProfile });
var configured = new SqlFormatterConfigurationResolver(catalog).Resolve(
    json, profileId: "project");
```

Приложение может передать свои пользовательские или проектные профили при создании каталога. Повторяющиеся идентификаторы, включая совпадение со встроенными, отклоняются. Неизвестный `profileId` даёт `TSF2000` и `Options == null`. Поля файла накладываются на профиль, а не сбрасывают его к `Default`. CLI и VSIX позволяют выбирать встроенные профили; VSIX также импортирует/экспортирует настройки. Именованные пользовательские профили в JSON и пользовательские ID профилей для CLI/VSIX пока не реализованы.

Для проверки сохранности комментариев отдельно запустите golden-набор:

```powershell
dotnet test tests/TSqlFormatter.GoldenTests/TSqlFormatter.GoldenTests.csproj --no-restore
```

Набор содержит 60 фиксированных случаев с ожидаемым SQL: ведущие, строчные, блочные и отдельно стоящие комментарии. Он проверяет, что каждый комментарий остаётся один раз, а повторное форматирование не меняет результат. Эта проверка также входит в `dotnet test TSqlFormatter.sln`.

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

Каждый `SqlCommentTrivia` содержит точный `Span`, исходный `Text`, `Kind` (`Line`/`Block`), `TokenIndex` и `Placement`: `Trailing` — после SQL-токена в той же строке, `Leading` — непосредственно перед следующим токеном (в том числе цепочка соседних комментариев), `Standalone` — без такого примыкания, например через пустую строку. `AnchorTokenIndex` указывает индекс связанного SQL-токена в `parsed.Tokens`; у `Standalone` он равен `null`. Сканер работает и с доступными токенами при ошибке разбора. Форматтер использует классификацию для строчных и блочных комментариев после запятых и перед следующей колонкой в `SELECT`, а также перед поддержанными предложениями; остальные позиции пока не получают отдельного правила размещения.

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

`FormatRequest` по умолчанию запрашивает весь документ (`Document`), диалект `Auto` и строгое поведение при ошибке разбора (`Strict`). Область `Selection` требует `SqlTextSpan` и форматирует только оператор верхнего уровня, содержащий выделение; небезопасные границы или несколько операторов дают `TSF3003` без изменений. Для `Statement` передайте позицию каретки как `new SqlTextSpan(offset, 0)` через аргумент `selection`: выбирается ближайший оператор верхнего уровня; неверная позиция или отсутствие оператора дают `TSF3004`, отсутствие позиции — `TSF3000`. В режиме `Strict` синтаксическая ошибка оставляет исходный текст без изменений и без правок: `ParseSucceeded == false`, одна или несколько диагностик `TSF1000` уровня `Error`. Если созданный форматтером SQL не проходит повторный разбор, исходный текст тоже сохраняется, а `TSF3001` возвращается как ошибка (`ParseSucceeded` при этом описывает успешный разбор исходного SQL).

Переданный `cancellationToken` проверяется при обходе AST, рендеринге и применении правок. Отмена вызывает `OperationCanceledException` вместо частичного `FormatResult`; синхронный вызов ScriptDom нельзя прервать посередине.

Экспериментальный `Safe` доступен через `new FormatRequest(parseFailureBehavior: ParseFailureBehavior.Safe)`. Когда весь скрипт не разбирается, он пытается форматировать только отдельные операторы, найденные в частичном AST и успешно разобранные изолированно; ошибочные операторы и промежутки между ними остаются исходными. Результат может иметь `Changed == true` при `ParseSucceeded == false` и сохраняет диагностики `TSF1000`; итоговый скрипт всё ещё может быть синтаксически неверным. При полностью валидном SQL `Safe` работает как `Strict`. CLI всегда использует `Strict` и не записывает частичный результат. `TokenFallback` пока не реализован: запрос с ним возвращает `TSF3002` без изменений.

`FormattingOptions` разделяет параметры на `General` (ширина 100, LF, без конечного перевода строки), `Indent` (4 пробела, без табуляции), `Keywords` (`Upper`), `Select` (`Auto`), `Clauses` (`Auto` для `GROUP BY` и `ORDER BY`), `Joins` и `Where` (переносы включены). Для поддержанных `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE` применяются отступ и EOL; ширина и параметры раскладки списков влияют на поддержанные списки `SELECT`. Параметры переносов JOIN/ON и WHERE/HAVING/AND/OR действуют только для поддержанных конструкций. `FormatResult` содержит итоговый текст, правки `TextEdit`, диагностики, признаки изменения и успешности разбора.

`FormattingOptions.Default` предоставляет те же значения, что и `new FormattingOptions()`. Метод `With` создаёт новый набор настроек с заменой только указанных секций, не изменяя исходный:

```csharp
var options = FormattingOptions.Default.With(
    general: new GeneralOptions(maxLineWidth: 120),
    keywords: new KeywordOptions(KeywordCase.Lower));
```

Публичный набор секций сейчас ограничен реально поддержанными параметрами. Настройки `CASE`, комментариев и других правил из плана пока не входят в API.

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

`IS NULL`, `IS NOT NULL`, `LIKE` и `NOT LIKE` поддержаны в тех же логических деревьях `WHERE`/`HAVING`/`ON`. Для `LIKE ... ESCAPE` и предикатов с комментариями внутри оператора исходная раскладка сохраняется, хотя регистр ключевых слов может измениться. Внутренние пробелы скалярных выражений сохраняются.

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

Поддержаны один или несколько простых CTE перед основным `SELECT`, `INSERT`, `UPDATE` или `DELETE`. Вложенный запрос каждого CTE получает отступ; список имён колонок, если он указан, сохраняется:

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

CTE с `XMLNAMESPACES`, вложенным `WITH`, неподдержанным выражением запроса или комментарием между блоком CTE и DML пока сохраняет исходную раскладку.

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

Аргументы самой функции сохраняют исходный текст. Простые рамки `ROWS`/`RANGE` с границами `BETWEEN` (безграничная, текущая строка или целочисленное смещение) либо одной такой границей выводятся отдельной строкой после `ORDER BY`. Рамки с комментариями, сложные границы, именованные окна и оконные функции внутри более сложного скалярного выражения сохраняют исходную раскладку; регистр распознанных ключевых слов может измениться.

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

Поддержаны несколько таких комментариев в одном списке. Пробел перед `--` нормализуется до одного, переводы строк создаются согласно `GeneralOptions.LineEnding`; сам текст комментария не меняется. Смежные комментарии после запятой и непосредственно перед следующей колонкой также остаются вместе с ней:

```sql
SELECT
    Id, -- разделитель
    -- поле для выгрузки
    Name
FROM T
```

Если между комментариями или между запятой и ведущим комментарием есть пустая строка, либо несколько блочных комментариев в одной строке дают неоднозначную привязку, исходная раскладка списка сохраняется; регистр распознанных ключевых слов всё же может измениться. Это правило не распространяется на комментарии внутри выражений колонок.

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

В `INSERT ... SELECT` вложенный запрос использует те же поддержанные правила форматирования `SELECT`. Порядок колонок и значений сохраняется. Поддержанный CTE перед `INSERT` форматируется над оператором. `INSERT ... EXEC`, `DEFAULT VALUES` и комментарии между строками значений пока сохраняют исходную раскладку; распознанные ключевые слова всё ещё могут менять регистр.

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

Порядок присваиваний сохраняется. Поддержанный CTE перед `UPDATE` форматируется над оператором. Составные присваивания вроде `+=` и `TOP` пока не форматируются структурно. Для неподдержанных конструкций сохраняется исходная раскладка, хотя регистр распознанных ключевых слов может измениться.

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

`DELETE TOP (целое число)` и поддержанный CTE перед `DELETE` форматируются структурно. Другие выражения `TOP` и комментарии внутри заголовка `DELETE` сохраняют исходную раскладку; регистр распознанных ключевых слов может измениться. Остальные ограничения `FROM` и `WHERE` совпадают с описанными для поддержанных `SELECT` и `UPDATE`.

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

- CLI выводит в stdout только stdin или один файл; каталоги и несколько файлов требуют `--write` либо `--check`. Поиск конфигурации доступен для файлов, пользовательские флаги настроек пока не реализованы.
- Структурное форматирование охватывает только описанные формы `SELECT`, `INSERT`, `UPDATE`, `DELETE` и `MERGE`; прочие конструкции сохраняют исходное расположение.
- Рендерер принимает готовое дерево `Doc`; сам по себе он не разбирает SQL.
