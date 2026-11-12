# Этап 10 / Stage 10 — 2026-11-12

## Русский

Реализованы пользовательские правила DEV/TEST/PROD и команда проверки опасных
операторов. Версии собираемых VSIX: VS 0.2.14 / SSMS 0.6.13. Существующие пакеты
в `release/` остаются выпуском этапов 1–9; в этом проходе они не пересобирались.

### Автоматические проверки

| Проверка | Результат |
|---|---|
| Release-сборка решения | 0 предупреждений, 0 ошибок |
| Core/Configuration/CLI | 1524 теста, все прошли |
| Golden | 145 тестов, все прошли; всего **1669** |
| Новые сценарии | **46**: SqlEnvironmentTests и DangerousSqlTests; контракты меню обновлены |
| RU/EN × light/dark UI-smoke | Все 4 варианта прошли: правила, сохранение/загрузка/отмена, отклонение пустой строки, удаление строки; PROD/контраст/навигация и ошибка разбора |
| Verify-Vsix | VS2022, VS2026, SSMS20, SSMS22 — пройден |

Проверены AND-условия, отсутствие контекста, явный offline-path, регистр,
приоритет PROD > TEST > DEV независимо от порядка, выбор первого цвета для
одинакового окружения, неизменяемая копия правил, неверные RGB/пределы/JSON,
миграция старых настроек и изменение контекста. SQL-сценарии включают десять
форм DROP, TRUNCATE, UPDATE/DELETE без WHERE, комментарии/литералы/идентификаторы,
динамический SQL, вложенные ветви/тела модулей, исходные позиции, ошибки разбора,
отключённый обычный анализ, UNKNOWN и отмену.

Исходные таблицы меню содержат 14 команд и 6 контекстных размещений. Новая команда
`SQLFormatter.CheckDangerousCommands` имеет ID `0x0280`; ревизия ресурса меню — 3.
Снимки standalone-диалогов `en-dark-safety-prod.png` и `ru-light-safety-prod.png`
просмотрены: все четыре находки, цветная метка, текст PROD и кнопки доступны.

### Установленная VS2022

Предыдущая копия расширения сохранена в `artifacts/stage10/installed-backup-VS2022/`.
VSIX 0.2.14 установлен штатным VSIXInstaller в VS2022 17.14.37710.0 x64.
Через штатный EnvDTE API открыт только синтетический файл
`artifacts/stage10/stage10-native-prod.sql`. Временное правило PROD сопоставляло
его явный moniker; сервер и БД были недоступны, соединение не открывалось.

Наблюдались красная метка **Environment: PROD**, отдельный акцент PROD и четыре
замечания TSQL006/TSQL007/TSQL004/TSQL005 на строках 2–5. Выбор DROP и **Go to**
перевёл курсор на строку **2**. Файл остался исходным, буфер — сохранённым без
правок. После проверки исходное значение EditorSettingsJson восстановлено;
синтетический документ закрыт. SQL не выполнялся.

Это проверка новой команды в VS2022, а не подтверждение всех поколений IDE или
живых подключений. Внутри VS2026/SSMS20/SSMS22 этап 10 в этом проходе не запускался.

### Границы реализации

- Цветная метка находится в диалоге явной проверки; вкладки не окрашиваются.
- При недоступных именах сервера/БД работают только подходящие явно заданные
  условия пути; без совпадения — UNKNOWN, а не DEV. Живой SSMS-контекст остаётся
  best-effort; чтение текущего сервера/БД на подключённом редакторе не проверялось.
- Существующие правила TSQL004/TSQL005 используются независимо от пользовательского
  отключения анализа. DROP/TRUNCATE TSQL006/TSQL007 относятся к новой команде,
  а не к обычному Analyze Document / CLI `--analyze`.
- Проверяются статические операторы, в том числе в определениях модулей и
  недостижимых ветвях. Динамический SQL не вычисляется; WHERE проверяется только
  на наличие. Метка не учитывает USE, трёхчастные имена и дальнейшие вызовы процедур.
- Нет перехвата исполнения или автоматического блокирования SQL. При изменении
  документа, снимка, контекста или настроек результат отклоняется; переход также
  повторно проверяет актуальность. Ошибка разбора показывает незавершённую проверку.

Логи и TRX: `artifacts/stage10/`; итог EnvDTE: `vs2022-result.json`, восстановление
настроек подтверждено в `native-vs2022.log`. Общие снимки UI: `artifacts/settings-ui/`.
Эти диагностические файлы не являются частью дистрибутива и не включаются в коммит.

Повторение: Release build/test решения; `scripts/Verify-SettingsUi.ps1 -SkipRestore`;
`scripts/Verify-Vsix.ps1` с identity/assembly/target своего адаптера. Для установленной
проверки используйте отдельный SQL-файл, правило пути PROD и команду проверки;
не выполняйте синтетический SQL, после проверки восстановите настройки.

## English

Stage 10 adds per-user DEV/TEST/PROD rules and **Check Dangerous Commands**.
Source VSIX versions are VS **0.2.14** / SSMS **0.6.13**; existing `release/`
packages still contain stages 1–9 and were not rebuilt in this pass.

Release builds with zero warnings/errors. All **1669 tests** pass (1524 Core/
Configuration/CLI and 145 golden), including **46 new scenarios**. All four
RU/EN × light/dark settings/safety-dialog variants and all four VSIX checks pass.
Tests cover AND matching, unavailable context, explicit offline paths, case,
overlap precedence, colors, limits, JSON migration, context changes, ten DROP forms,
TRUNCATE and unfiltered writes, false positives, nested code, positions, parse
errors, cancellation and independence from disabled normal analysis. Menu contracts
contain 14 commands, six context placements and the new command ID **0x0280**;
menu resource revision is 3. The English dark and Russian light safety-dialog
captures were visually inspected.

In installed **VS2022 17.14.37710.0 x64**, VSIX **0.2.14** was installed with the standard
installer after backing up the previous copy. A synthetic file was opened through
EnvDTE and matched a temporary PROD document rule. The dialog showed its red PROD
label, separate warning and four findings on lines 2–5. Selecting DROP and **Go to**
navigated to **line 2**. File contents remained unchanged and the buffer stayed
saved. Original editor settings were restored and the test document closed. No SQL
execution or database connection was used.

This installed check covers VS2022, not VS2026/SSMS20/SSMS22 or live SSMS metadata.
The latter remains best effort. Missing names never imply DEV. The indicator lives
in the explicit check dialog rather than document tabs. TSQL004/TSQL005 always run
for this command; TSQL006/TSQL007 are not added to normal analysis or CLI defaults.
Static module bodies and inactive branches are included; dynamic SQL and predicate
truth are not evaluated. USE, three-part names and procedure calls do not update
the captured context label. No execution interception or blocking is implemented.
Stale snapshots/context/settings are rejected and navigation rechecks them; parse
errors are incomplete checks, never clean results.

Logs, TRX and installed-host results are in `artifacts/stage10/`; standalone UI
captures are in `artifacts/settings-ui/`. They are local diagnostics, excluded from
the commit. Reproduce with the solution build/test, settings smoke and per-adapter
VSIX check; installed checks should use synthetic files and restore original rules.
