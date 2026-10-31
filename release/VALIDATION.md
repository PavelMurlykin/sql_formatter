# Проверка выпуска VS 0.2.13 / SSMS 0.6.12 — 2026-10-31

Текущие файлы в `release/` включают завершённые этапы 1–9, в том числе настройки
анализа и историю закрытия. Это локальные preview-пакеты без публикации и подписи.

| Файл | Версия | Цель |
|---|---|---|
| TSqlFormatter.VS2022.vsix | 0.2.13 | VS 17.14–17.x, amd64 |
| TSqlFormatter.VS2026.vsix | 0.2.13 | VS 18.x, amd64 |
| TSqlFormatter.SSMS20.vsix | 0.6.12 | SSMS 20, x86; комплектный Install-SSMS20.ps1 |
| TSqlFormatter.SSMS22.vsix | 0.6.12 | SSMS 22.x, amd64 |
| TSqlFormatter.Tool.0.1.0-preview.8.nupkg | 0.1.0-preview.8 | CLI, .NET 10 Runtime |

## Автоматические проверки текущих пакетов

Windows, .NET SDK 10.0.401, Release. `scripts/Build-Release.ps1 -SkipRestore`
завершился успешно; сборка решения — **0 ошибок, 0 предупреждений**.

| Проверка | Результат |
|---|---|
| Решение | 1478 Core/Configuration/CLI + 145 golden = **1623**, все прошли |
| Verify-Vsix | Все 4 пакета: identity, диапазон хоста, архитектура и обязательные библиотеки |
| Профили из готовых VSIX | **228 сочетаний**: 4 пакета × 3 профиля × 19 SQL-примеров; импорт, хранение, токены, идемпотентность; одинаковый вывод для 57 сочетаний профиля/SQL |
| Этапы 3 и 9 в библиотеках VSIX | Все 4 пакета: настройки анализа/истории, проектные переопределения, отключение анализа, несохранённый буфер закрытия, метаданные, возраст/количество и отключение истории |
| Общая форма настроек | RU/EN × light/dark — все 4 сценария, включая привязку/сохранение/отмену настроек анализа и истории |
| Установщик SSMS20 | Миграция управляемых копий, повторное обновление, запрет копий внутри IDE и отказ от перемещения неуправляемых каталогов |
| Упакованный CLI | Локальная установка preview.8; форматирование stdin с кодом 0 и анализ SELECT * с TSQL001 и кодом 1 |

Проверка готовых VSIX извлекает библиотеки в отдельные каталоги `artifacts/`,
запускает .NET Framework-хост x86 для SSMS20 и x64 для остальных пакетов,
подтверждает загрузку именно комплектных Core/Configuration. Для SSMS заранее
загружается установленный ScriptDom; комплектный ScriptDom 18 используется движком.
Эта проверка библиотек отдельно от служб редактора внутри IDE.

В поставку скопирован исправленный `Install-SSMS20.ps1`: резервные копии находятся
в `%LOCALAPPDATA%\TSqlFormatter\SSMS20\Backups`, вне рекурсивных путей загрузки
оболочки. Управляемые копии прежнего установщика внутри IDE переносятся туда же.
Три нативных JSON-профиля сохранены без изменения настроек; анализ и история
не входят в профили форматирования. Хэши всех 9 установочных файлов/профилей
содержатся в [release-manifest.json](release-manifest.json); отдельные хэши четырёх
VSIX и результаты профилей/этапов 3, 9 — в [profile-verification.json](profile-verification.json).

Лог конвейера: `artifacts/release-2026-10-31/build-release.log`,
TRX: `artifacts/release-tests/`, SQL и логи пакетов:
`artifacts/release-profile-smoke/`, снимки формы: `artifacts/settings-ui/`.

## Установленные хосты: отдельные результаты реализации

**Свежая установка именно VS 0.2.13 / SSMS 0.6.12 в этом проходе не выполнялась.**
Ни сборка, ни проверки извлечённых библиотек не подтверждают установку и работу
служб IDE. Следующая таблица относится к проверке реализации этапов 3 и 9
2026-10-31 до обновления версий установочных пакетов; детали и ограничения — в
[отчёте этапов 3 и 9](../docs/development/stages-3-9-validation.md).

| Ранее проверенный установленный хост | Подтверждённые сценарии |
|---|---|
| VS2022 17.14 x64 | Сохранение, отброшенные правки и несохранённый SQL; отчёт TSQL001; выключение анализа сохранено/перечитано, исходное включение восстановлено |
| VS2026 18.10 x64 | Три сценария истории; регистрация 13 команд и вызов анализа; отдельный цикл сохранения настроек не повторялся |
| SSMS20 20.2 x86 | Три сценария истории; 13 команд, отчёт TSQL001 Warning; правильный CodeBase после переноса старых резервных копий |
| SSMS22 22.10 x64 | Три сценария истории; 13 команд, отчёт TSQL001 Warning; реальные элементы анализа и лимитов истории в настройках |

SQL и подключения к серверу в этих сценариях не выполнялись. Сохранение имён
живого сервера/БД не проверялось; они необязательны. Поля метаданных покрыты
автоматическим round-trip. Восстановление в новом документе не проходило отдельный
повторный UI-цикл в каждом поколении. Эти границы сохраняются для текущего выпуска.

## Повторение и проверка после установки

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -SkipRestore
```

Без восстановленных зависимостей уберите `-SkipRestore`. Скрипт не устанавливает
расширения в IDE. Следуйте [RU](README.ru.md) / [EN](README.en.md) инструкции,
перезапустите IDE и на отдельных тестовых SQL проверьте Analyze Document,
сохранение/повторное открытие настроек, сохранение и закрытие с отказом от записи,
новый несохранённый SQL и Restore Closed Script. SQL выполнять не требуется.
Проверьте активную вкладку и проектную секцию `analysis`, если ожидаемое правило
не появляется. Для SSMS20 замените старый установочный скрипт комплектным.

## English

The current VS **0.2.13**, SSMS **0.6.12** and CLI **0.1.0-preview.8** packages
include completed stages 1–9. The Release pipeline passed with zero warnings/errors,
**1623 solution tests**, four VSIX checks, **228** extracted-package profile cases,
four extracted-package analysis/history feature probes, four RU/EN/theme UI variants,
the SSMS20 installer regression and packaged CLI formatting/analysis smoke.
All 57 profile/SQL outputs match across the four packages. Core and Configuration
load from each extracted installer; SSMS20 runs in x86 and the others in x64.

The bundled SSMS20 installer keeps and migrates managed backups outside the IDE
package search paths. The three native profiles are unchanged. SHA-256 values and
package-scoped results are recorded in the JSON manifests linked above.

**Fresh installation of these exact new versions was not repeated.** The installed
host table records earlier 2026-10-31 validation of the stage 3/9 implementation:
save/discarded/untitled history passed in VS2022, VS2026, SSMS20 and SSMS22; analysis
reports were observed in VS2022/SSMS20/SSMS22, with invocation recorded in VS2026.
VS2022 settings persistence and the native SSMS22 settings controls were checked.
These are separate from the current packages' automated checks. No SQL execution or
database connection was used. Live server/database capture and a separate restore
UI cycle in every host remain unverified. Follow the installation guides and repeat
the offline lifecycle checks after upgrading. Stages 10–11 and the final stage 12
remain planned; these are unsigned local previews with no public publication.

---

Ниже — исторические результаты прежних пакетов; они не описывают текущие VSIX.

## Историческая проверка VS 0.2.12 / SSMS 0.6.11 — 2026-10-08

Windows, .NET SDK 10.0.401, Release: **0 предупреждений, 0 ошибок**.
Пройдены **1282 Core + 145 golden = 1427 теста**, проверки всех четырёх VSIX,
упаковка/локальная установка/stdin-проверка CLI **0.1.0-preview.7**.

| Файл | Версия | Цель |
| --- | --- | --- |
| TSqlFormatter.VS2022.vsix | 0.2.12 | VS 17.14–17.x, amd64 |
| TSqlFormatter.VS2026.vsix | 0.2.12 | VS 18.x, amd64 |
| TSqlFormatter.SSMS20.vsix | 0.6.11 | SSMS 20, x86, установочный скрипт |
| TSqlFormatter.SSMS22.vsix | 0.6.11 | SSMS 22.x, amd64 |

## Исправления и автоматические проверки

В ADIR_SQL_Main включены `syntax.removeOptionalBrackets`, `syntax.addSemicolons`
и `code.if.breakAfterElseIf=never`. Новые синтаксические правила по умолчанию выключены.
Сохранённый пользовательский профиль требует повторного импорта JSON из `release/`.
Проверены объекты БД/схем/таблиц/процедур, Unicode/временные объекты, обязательные
скобки, соседние имя колонки/псевдоним, сочетание с настройками регистра,
строки/комментарии, GO, процедуры, представления, табличные и скалярные
функции, INSERT EXEC, метки/GOTO, TRY/CATCH и существующие разделители.
Повторное форматирование не добавляет лишние `;` к END тела функции.

Точный пример пользователя с нарастающими табуляциями ELSE IF теперь выравнивается
на уровне первого IF, с одним шагом в телах BEGIN/END. Проверяются отдельные ветви
без BEGIN/END, настоящие вложенные IF, неизменность областей formatting off/on и
текста за границей выделения. Выделенный идентификатор или WHERE не получает
разделитель своего внешнего оператора. Выделения запроса VIEW/табличной функции и
смеси фрагмента WHERE с полным оператором также сохраняют правильные границы.
Разбор и проверка разрешённых изменений
токенов отвергают подмену имени и дополнительные разделители.

Три профиля проверены с библиотеками из каждого готового VSIX: **228 сочетаний**
(4 пакета × 3 профиля × 19 SQL-примеров), включая x86 SSMS 20. Проверены импорт,
редактор/хранение, разбор, разрешённые изменения токенов, идемпотентность и одинаковый
вывод всех пакетов для 57 сочетаний. SHA-256 и результаты находятся в
`release-manifest.json` и `profile-verification.json`.

Общий WinForms-редактор прошёл сценарии ru/en × light/dark: локализация всех полей,
четыре жирных группы и 12 страниц, детали/поиск, две вкладки, сохранение/переключение
профилей, горячие клавиши, подсветка и редактирование SQL-примера. Новые правила
находятся справа на общей странице, дополнительные уровни дерева не появились.
Снимки ru/light и en/dark просмотрены; файлы находятся в `artifacts/settings-ui/`.

Логи: `artifacts/adir-release-build.log`, `artifacts/adir-core-tests.log`,
`artifacts/release-profile-smoke/`. Воспроизведение после restore:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -SkipRestore
```

## Проверка в установленной VS2022

**Пакеты 0.2.12 / 0.6.11 в установленных IDE в этом проходе не запускались.**
Сборка и отдельный UI-хост не подтверждают работу сервисов IDE. После установки
повторно импортируйте ADIR_SQL_Main, выберите его активным и сохраните. Если используется
`.tsqlformatter.json`, проверьте новые правила в нём. В отдельном тестовом SQL проверьте
USE/SELECT/EXEC со скобками и без `;`, пользовательскую цепочку ELSE IF, повторный
вызов, выделение WHERE и Undo. SQL в автоматических сценариях не исполнялся;
новая сверка с установленным SQL Prompt или пользовательским корпусом не проводилась.

English: Four release packages, 1427 tests, 228 extracted-package profile cases,
four standalone language/theme UI scenarios and CLI package smoke passed.
Optional brackets and missing statement terminators are enabled in the updated
ADIR_SQL_Main; reimport saved profiles. ELSE IF chains stay aligned. Installed-host
validation and a new SQL Prompt corpus comparison were not performed.

---

Ниже приведены исторические результаты для предыдущих версий.

## Историческая проверка VS 0.2.11 / SSMS 0.6.10 — 2026-10-06

## Автоматические проверки текущих пакетов

Windows, .NET SDK 10.0.401, Release. Сборка всех четырёх адаптеров:
**0 предупреждений, 0 ошибок**. **1248 Core + 145 golden = 1393 теста**.
Проверены идентификаторы, диапазоны хостов, архитектуры и обязательные библиотеки VSIX;
упаковка, локальная установка и stdin-сценарий CLI также пройдены.

| Файл | Версия | Цель |
| --- | --- | --- |
| TSqlFormatter.VS2022.vsix | 0.2.11 | VS 17.14–17.x, amd64 |
| TSqlFormatter.VS2026.vsix | 0.2.11 | VS 18.x, amd64 |
| TSqlFormatter.SSMS20.vsix | 0.6.10 | SSMS 20, x86, установочный скрипт |
| TSqlFormatter.SSMS22.vsix | 0.6.10 | SSMS 22.x, amd64 |

Общий WinForms-редактор проверен в четырёх сочетаниях: ru/en × light/dark.
Сценарий проверяет четыре жирных заголовка групп и 12 страниц с одним уровнем
вложенности, увеличенные интервалы, доступ ко всем полям объединённой страницы,
разделы справа при поиске и соответствующий автоматический SQL-пример в обеих темах.
Изменение цвета RichEdit не считается правкой пользовательского примера.
Также проверены две вкладки, группировка всех правил, стиль и размер отступа
на общей странице, ограничение размера отступа 10, поиск, числовые изменения,
синхронизацию выбора профиля, сохранение без закрытия, сохранение профиля по умолчанию,
сохранение черновика при переключении, захват Ctrl+Alt+F в поле сочетания,
подсветку SQL, очистку устаревшего результата, ошибочный SQL и минимальный размер окна.
Отдельно проверено, что несохранённые изменения не меняют сохранённый снимок.
Снимки вкладок, объединённой страницы подробностей и тем: `artifacts/settings-ui/`; они просмотрены визуально.

Тесты команды редактора проверяют отсутствие выделения, повторные вызовы,
выделение нескольких операторов, списка выражений и WHERE, сохранность текста
за границами выделения, разбор результата, повторяемость, отказ при разрезании
токена и обработку выделенного оператора при ошибочном SQL снаружи.
Тесты обеих VSCT-таблиц проверяют две команды меню и общую команду контекстного меню.

Профили `ADIR_SQL_Main.json`, `AV_Profile.json`, `Right-aligned-EPM-AWB2.json`
проверены с библиотеками каждого готового VSIX: **180 сочетаний** (4 пакета ×
3 профиля × 15 SQL-примеров), включая x86 для SSMS 20. Проверены импорт, хранение,
разбор, токены и идемпотентность. Все четыре пакета дают одинаковый вывод для
45 сочетаний SQL/профиля. SHA-256 находятся в `release-manifest.json` и
`profile-verification.json`; логи — `artifacts/compact-navigation-release.log` и
`artifacts/release-profile-smoke/`.

Воспроизведение:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -SkipRestore
```

Без предварительного восстановления уберите `-SkipRestore`.

## Проверка внутри установленных IDE: текущая версия

**VS 0.2.11 и SSMS 0.6.10 в установленных IDE в этом проходе не запускались.**
WinForms-сценарий и проверки библиотек не подтверждают работу сервисов оболочки.
Повторное форматирование после смены фокуса, контекстное меню, локаль DTE,
назначение/конфликты/сохранение реальных горячих клавиш, Undo и получение цветов
реальной темы VS требуют проверки в установленной VS 2022 17.14.

Рекомендуемый сценарий в новом тестовом `.sql`: выполнить форматирование три раза;
открыть Output и снова форматировать; проверить выбор второй SQL-вкладки;
выделить WHERE и два оператора, проверить неизменность соседнего текста;
проверить контекстную команду, Ctrl+Z и сохранение; назначить сочетание,
перезапустить IDE; проверить ru/en и Light/Dark/Blue, сохранение и повторное
открытие профиля. Пользовательские SQL не использовались и не исполнялись.

English: Release build (zero warnings/errors), 1393 tests, four VSIX checks,
all four shared UI language/theme scenarios, packaged CLI smoke and 180 profile
cases passed. New adapters were not installed/run inside IDEs in this pass;
real document focus, command placement, shortcuts and IDE theme/locale services
remain unverified there. The following records concern earlier versions only.

---

## Историческая проверка 0.2.9 / 0.6.8 — 2026-10-05

## Сборка и установочные файлы

Windows, .NET SDK 10.0.401, Release. Сборка решения: 0 предупреждений,
0 ошибок. Все 1238 Core-тестов и 145 golden-тестов пройдены (1383 всего).
Проверены содержимое, идентификатор, целевой диапазон и архитектура каждого VSIX.
WinForms-сценарий настроек и установка/запуск упакованной CLI также пройдены.

| Файл | Версия | Цель манифеста |
| --- | --- | --- |
| TSqlFormatter.VS2022.vsix | 0.2.9 | VS 17.14–17.x, amd64 |
| TSqlFormatter.VS2026.vsix | 0.2.9 | VS 18.x, amd64 |
| TSqlFormatter.SSMS20.vsix | 0.6.8 | legacy ssms shell; скрипт проверяет ProductMajorPart = 20 |
| TSqlFormatter.SSMS22.vsix | 0.6.8 | SSMS 22.x, amd64 |

SSMS 20 использует адаптер, собранный с Microsoft.VisualStudio.SDK 15.0.1,
и запускается в x86. Остальные адаптеры используют SDK 17.14.40265 и x64.
Core/Configuration и нативные профили общие для всех четырёх пакетов.

## Три профиля во всех четырёх пакетах

Проверяются `ADIR_SQL_Main.json`, `AV_Profile.json`, `Right-aligned-EPM-AWB2.json`.
Первые настройки подготовлены по SQL Prompt, два других профиля — по SQL Complete.
Профили AV/EPM сохраняют по 573 правила.

Проверочный .NET Framework 4.7.2 процесс использует библиотеки, извлечённые из
каждого готового VSIX, и проходит 15 SQL-примеров для каждого профиля:
SELECT, процедура, таблица, DECLARE, INSERT, CASE, CTE, VIEW, JOIN, UPDATE/DELETE,
подзапрос, IN, комментарии, GO и оконная функция. Для SSMS 20 он запускается в x86,
для остальных — в x64. В сценарии SSMS 20 предварительно загружен установленный
ScriptDom 16.1; форматирование использует комплектный ScriptDom 18.

Проверены импорт нативного JSON, сохранение и загрузка настроек, сериализация
именованного профиля по умолчанию, успешный разбор вывода, сохранность токенов
(с допустимой нормализацией скобок идентификаторов) и идемпотентность.
Все **180 сочетаний** пройдены. Хэши всех 45 SQL-результатов совпали между четырьмя
пакетами. Это проверка библиотек установочного файла, отдельно от интерфейса IDE.

Воспроизведение из корня проекта:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1
```

Только профили после предварительного восстановления зависимостей:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-ReleaseProfiles.ps1
```

`profile-verification.json` содержит SHA-256 профилей и проверенных VSIX.
Логи и SQL-результаты остаются в `artifacts/release-profile-smoke/`.

## Проверка установленных IDE

| Хост | Текущая проверка |
| --- | --- |
| VS Community 2022 17.14.41 x64 | Пакет 0.2.9 установлен; меню Extensions → SQL Formatter доступно. Format Document, сохранение и разные раскладки всех трёх профилей проверены. Один Undo вернул исходный SQL для ADIR. |
| SSMS 20.2.1 (20.2.37.0), x86 | Пакет 0.6.8 установлен скриптом; верхнее меню SQL Formatter доступно. Format Document и сохранение проверены со всеми тремя профилями без подключения. Один Undo в активном редакторе вернул исходный SQL для ADIR. Окно «Сохранённые профили» и диалог импорта открылись; сохранение импортированного профиля через хост в этом запуске не проверялось. |
| VS Community 2026 18.10.3 x64 | Новый пакет 0.2.9 проверен сборкой, содержимым и профилями в извлечённых библиотеках; свежая установка и интерфейс в этом запуске не проверялись. |
| SSMS 22.10.2 x64 | Новый пакет 0.6.8 проверен сборкой, содержимым и профилями в извлечённых библиотеках; свежая установка и интерфейс в этом запуске не проверялись. |

Сборки адаптеров в установленных VS 2022 и SSMS 20 совпадают по SHA-256 с Release.
В VS 2022 и SSMS 20 применялась копия каждого профиля как ближайший `.tsqlformatter.json`.
Сохранённый контрольный SELECT совпадает с выводом комплектного движка после
нормализации окончания строк и завершающего пробела/перевода строки редактора.
Проверочные файлы: `artifacts/compatibility/VS2022-*.sql` и `SSMS20-*.sql`. Пользовательские SQL и
серверные данные не менялись; SQL не выполнялся.

Исторические проверки предыдущих версий описаны в
[матрице VS](../docs/visual-studio-smoke.md) и
[матрице SSMS 22](../docs/ssms22-compatibility.md). Они не подтверждают новые пакеты
или все минорные версии хостов. Проверка библиотек не заменяет проверку всех
команд, сохранения профилей через конкретную IDE и длительной работы в ней.
Приближения исходных профилей описаны в руководствах; идентичность выводу сторонних
форматтеров не заявляется. Пакеты локальные, без публикации и цифровой подписи.

English: the Release build, 1383 tests, four package checks, settings UI smoke,
packaged CLI smoke and all 180 extracted-package profile cases passed. VS 2022 and SSMS 20
Document formatting was checked with all three profiles. Installed-host coverage
is limited to the exact versions and actions in the table; fresh VS 2026/SSMS 22
UI checks are not claimed.
