# Этапы 3 и 9 / Stages 3 and 9 — 2026-10-31

## Русский

Отчёт фиксирует проверку исходников ветки `feature/extra_functions` до обновления
версий пакетов. Текущая поставка описана отдельно в
[проверке выпуска](../../release/VALIDATION.md); установленные результаты ниже
не являются повторной установкой новых версий.
Успешная сборка отдельно не считается проверкой установленного расширения.

### Автоматические проверки

| Проверка | Результат |
|---|---|
| Release-сборка решения | 0 ошибок, 0 предупреждений |
| Core/Configuration/CLI | 1478 тестов, все прошли |
| Golden | 145 тестов, все прошли; всего 1623 |
| Настройки RU/EN × светлая/тёмная тема | Все 4 варианта прошли; привязка анализа/истории, загрузка, изменение, отмена и сериализация |
| Verify-Vsix | VS2022, VS2026, SSMS20, SSMS22 — пройден |
| Установщик SSMS20 | Миграция старых копий, повторное обновление, запрет копий внутри IDE, отказ от перемещения неуправляемых каталогов — пройдены |

Добавлены 22 сценария в `EditorFeaturesSettingsTests`,
`EditorHistoryLifecycleTests`, `EditorAnalysisIntegrationTests` и проверки
привязки в `TSqlFormatter.SettingsUiSmoke`. Ротация проверяет точную границу UTC,
число снимков и отключённый сбор. Тесты интеграции анализа проверяют discovery,
приоритет явно указанных полей проекта и отключение до разбора SQL.
Регрессия установщика: `tests/TSqlFormatter.InstallerSmoke/Verify-SSMS20Backup.ps1`.

### Установленные хосты

Синтетические `.sql` файлы открывались через штатный EnvDTE API. Проверка изменяла
только свои файлы в `artifacts/stage39/host-tests/`, сохраняла документ, добавляла
маркер отброшенных правок и закрывала с отказом от сохранения. Затем создавался
новый несохранённый SQL-документ и закрывался так же. Для каждого хоста проверены
три отдельных JSON-снимка с нужным текстом и причинами `save` / `close`.
Подключения к БД и выполнение SQL не использовались.

| Хост | Сохранение | Отброшенные правки | Несохранённый SQL | Анализ и настройки |
|---|---|---|---|---|
| VS2022 17.14, x64 | Да | Да | Да | Отчёт TSQL001; выключение анализа сохранено и подтверждено повторным открытием, исходное включение восстановлено |
| VS2026 18.10, x64 | Да | Да | Да | 13 команд зарегистрированы, Analyze Document вызвана; отдельный цикл сохранения настроек в этом хосте не повторялся |
| SSMS20 20.2, x86 | Да | Да | Да | 13 команд зарегистрированы, отчёт TSQL001 с Warning |
| SSMS22 22.10, x64 | Да | Да | Да | 13 команд зарегистрированы, отчёт TSQL001 с Warning, реальная форма настроек показывает анализ, правила и все лимиты истории; закрыта без изменений |

В SSMS22 канонические имена команд с размещением в контекстном меню начинаются
с `EditorContextMenus.CodeWindow.SQLFormatter.`. Проверяющий код должен учитывать
этот префикс; поиск только по началу `SQLFormatter.` пропускает эти команды.
SSMS20 публикует DTE под GUID-moniker, а не `VisualStudio.DTE.*`.

Во время установленных проверок устранены два дефекта: ожидание завершения
фоновой записи больше не ждёт собственной коллекции задач; установщик SSMS20
больше не оставляет резервные `.pkgdef` внутри рекурсивных путей поиска оболочки.
Старые управляемые копии перенесены в `%LOCALAPPDATA%\TSqlFormatter\SSMS20\Backups`.
Реальный `CodeBase` после обновления указывает на `Extensions\TSqlFormatter.Ssms20`.
Ревизия ресурса меню увеличена до 2 для изменившегося набора команд.

### Границы проверки и повторение

Сервер/БД необязательны. Сериализация этих полей покрыта регрессией, наличие
`ServiceCache.ScriptFactory` подтверждено в локальной `SqlPackageBase.dll` SSMS22.
Получение имён из живого подключённого редактора не проверялось. Отсутствие
контекста не препятствует снимку. Пароли и строки подключения не читаются.
Автоматический анализ при наборе и аварийное восстановление после падения процесса
не входят в эти этапы; история снимается при сохранении и штатном закрытии.
Поведение восстановления в новом документе не проходило отдельный повторный
цикл UI-проверки в каждом поколении; механизм восстановления не менялся.

Команды повторения: `dotnet build TSqlFormatter.sln -c Release`,
`dotnet test TSqlFormatter.sln -c Release`,
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-SettingsUi.ps1`,
`powershell -NoProfile -ExecutionPolicy Bypass -File tests/TSqlFormatter.InstallerSmoke/Verify-SSMS20Backup.ps1`.
`Verify-Vsix.ps1` запускается с отдельными identity/assembly/target для каждого адаптера.
После установки повторите три сценария закрытия из таблицы на тестовых файлах.

Локальные диагностические логи, TRX, результаты хостов и резервные копии находятся
в `artifacts/stage39/`; снимки формы — в `artifacts/settings-ui/`. Эти промежуточные
файлы не являются дистрибутивами и не включаются в коммит.

## English

This report records `feature/extra_functions` source validation before the package
version update. The current delivery is described separately in
[release validation](../../release/VALIDATION.md); the installed-host evidence below
does not claim fresh installation of the new versions. A successful build alone is
not installed-host validation.

The Release solution builds with no warnings or errors. All **1623 tests** pass
(1478 Core/Configuration/CLI and 145 golden), as do four RU/EN × light/dark settings
smoke variants, all four VSIX checks, and the SSMS20 installer regression. The latter
covers legacy-backup migration, repeated upgrades, unsafe backup paths and unmanaged
folders. The 22 new unit/integration scenarios cover settings persistence, project
precedence, disabled analysis, cached close-time text, optional metadata and UTC/count
retention boundaries. UI smoke checks binding and draft/save/cancel behavior.

| Installed host | Save / discarded edits / untitled SQL snapshots | Analysis and settings evidence |
|---|---|---|
| VS2022 17.14 x64 | All three passed | TSQL001 report; disabled setting persisted across reopening, then original enabled state restored |
| VS2026 18.10 x64 | All three passed | 13 commands registered, Analyze Document invoked; a separate native settings-persistence cycle was not repeated |
| SSMS20 20.2 x86 | All three passed | 13 commands registered, TSQL001 Warning report |
| SSMS22 22.10 x64 | All three passed | 13 commands registered, TSQL001 Warning report; native settings form showed analysis/rules/all history limits and was closed without changes |

Tests used the official EnvDTE API and synthetic files under
`artifacts/stage39/host-tests/`. They saved text, added discarded edits, closed with
SaveChangesNo, and closed a new untitled SQL document the same way. Three distinct
JSON snapshots per host were checked for content and `save` / `close` reasons.
No database connections or SQL execution were used.

SSMS22 context-placed command names use the
`EditorContextMenus.CodeWindow.SQLFormatter.` prefix. SSMS20 exposes DTE under a GUID
moniker. Checking only `SQLFormatter.*` or `VisualStudio.DTE.*` respectively misses
these registrations. Installed checks also exposed and fixed a background-task drain
self-wait and SSMS20 backups being recursively discovered as old package definitions.
Managed backups now live outside the IDE under
`%LOCALAPPDATA%\TSqlFormatter\SSMS20\Backups`; the actual updated CodeBase points to
`Extensions\TSqlFormatter.Ssms20`. The menu resource revision is now 2.

Server/database metadata is optional. Its JSON round trip is tested, and the local
SSMS22 `SqlPackageBase.dll` confirms `ServiceCache.ScriptFactory` exists. Live connected
editor metadata capture was not exercised. Missing context does not prevent snapshots;
passwords and connection strings are not read. Analysis while typing and recovery
after a process crash are outside these stages. Restoring into a new document was not
separately re-tested through every host UI; that mechanism was unchanged.

Reproduce with the solution build/test commands and the settings/installer PowerShell
scripts listed above, plus `Verify-Vsix.ps1` for each adapter. After installation,
repeat the three lifecycle scenarios on test files. Local logs, TRX and host results
are under `artifacts/stage39/`; UI captures are under `artifacts/settings-ui/`.
They are diagnostics rather than distributables and are excluded from the commit.
