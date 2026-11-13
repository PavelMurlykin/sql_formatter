# Этап 11 / Stage 11 — 2026-11-13

## Русский

Реализованы три команды работы с текстом TSV из буфера обмена: IN одного столбца,
INSERT с целевой таблицей и статистика числовых ячеек. Версии текущих исходников:
**VS 0.2.15 / SSMS 0.6.14**. Пакеты в `release/` в этом проходе не обновлялись:
они остаются поставкой этапов 1–10, VS 0.2.14 / SSMS 0.6.13.

### Автоматические проверки

| Проверка | Результат |
|---|---|
| Release-сборка решения | 0 ошибок, 0 предупреждений; собраны все четыре адаптера |
| Core/Configuration/CLI | 1583 теста, все прошли, без пропусков |
| Golden | 145 тестов; всего **1728** |
| Новые сценарии | **59** в QueryResultsTests; контракты меню обновлены |
| RU/EN × light/dark UI-smoke | Все 4 варианта прошли: явные заголовки/текстовый режим, имя таблицы и локализованные ошибки, статистика, предпросмотр в обычном/минимальном размере |
| Verify-Vsix | VS2022, VS2026, SSMS20, SSMS22 — identity, диапазоны, архитектуры и состав пройдены |

Тесты покрывают CRLF/LF/CR, пустые ячейки, кавычки/встроенные табуляции и переносы,
неверный TSV, лимиты, явные заголовки, IN без потери дубликатов/NULL, числа/даты/
Unicode-строки, ведущие нули и смешанные столбцы, экранирование инъекций в данных
и именах, отклонение произвольного SQL вместо таблицы, разбиение INSERT 1001 строки,
суммарную точность числового столбца, три комплектных профиля и нижний регистр,
инвариантную статистику, точность/переполнение значений и суммы, отмену вычисления.

Обнаружен и исправлен существующий дефект TransformationPreviewWindow: первая
строка пряталась под заголовком, а кнопки с абсолютными координатами уходили за
панель. Общие проверки теперь требуют видимой первой строки и размещения кнопок
внутри панели при обычном и минимальном размерах. Снимки UI в
`artifacts/settings-ui/*-tsv-{options,statistics,preview}.png`; диалоги просмотрены.

Обе таблицы меню содержат **17 команд / 9 контекстных размещений**, IDs
**0x0290–0x0292**, ревизия ресурса меню — **4**. Команды есть в обоих адаптерах;
два проекта каждого семейства используют общие исходники.

### Установленная VS2022

Проверка выполнена в **VS2022 17.14.37710.0 x64**, расширение **0.2.15**.
Предыдущая версия сохранена в `artifacts/stage11/installed-backup-VS2022/`.
Сначала использован штатный VSIXInstaller. После исправления предпросмотра при
закрытой IDE обновлена DLL адаптера из окончательного VSIX; SHA-256 установленного
адаптера, Core и Configuration сверены с содержимым пакета.

Через штатный EnvDTE API открыт только синтетический `stage11-native.sql`,
команды вызваны по зарегистрированным именам; диалоги проверены в интерфейсе IDE:

1. TSV `1`, `2`, `NULL` вставлен у курсора как `IN (1, 2, NULL)`; один Undo
   восстановил исходный SQL.
2. TSV с заголовками Id/Name и именем `О'Брайен` заменил выделение на INSERT
   в `[dbo].[Stage11_TestOnly]`; апостроф экранирован, NULL сохранён. Один Undo
   вернул всё выделение.
3. Отмена предпросмотра `IN (4, 5)` оставила исходный текст.
4. Статистика `1`, `2.5`, `NULL`, `text`: всего 4 ячейки, числовых 2, сумма 3.5,
   среднее 1.75. SQL и тестовый текст буфера не изменились.

После проверки исходный системный буфер обмена восстановлен, синтетический
документ закрыт. Итоговый SQL остался исходным, буфер редактора — сохранённым.
SQL не исполнялся, подключение не открывалось. Итог —
`artifacts/stage11/vs2022-result.json`; журнал — `native-vs2022.log`, хэши —
`installed-vs2022-hashes.json`. Диагностика не включается в коммит.

### Границы и повторение

- Внутри VS2026/SSMS20/SSMS22 этап 11 отдельно не запускался; сборка/упаковка
  и общий UI не заменяют установленную проверку служб этих IDE.
- Источник — обычный текст TSV из буфера, без COM ResultsGrid и HTML/CSV.
  Реальный подключённый грид результатов не использовался; данные синтетические.
- Схема БД, IDENTITY/ограничения и соответствие целевых типов не проверяются.
  Столбцы/NULL/даты выводятся по документированным правилам, не по метаданным.
- Вставка работает по захваченному документу/снимку/курсору или обычному выделению;
  прямоугольные многодиапазонные выделения отклоняются. Буфер SQL — до 16 Ми символов.
- Пределы TSV: 1 Ми символов, 10000 строк с заголовком, 256 колонок, 100000 ячеек.
  Условие первой строки и текстовый режим задаются для каждого вызова.
- Статистика допускает только точные decimal-значения и итоговую сумму; среднее
  округляется decimal-делением. CLI-команд для этих операций нет.

Повторение: Release build/test решения; `scripts/Verify-SettingsUi.ps1 -SkipRestore`;
`scripts/Verify-Vsix.ps1` с параметрами каждого адаптера. Логи и TRX —
`artifacts/stage11/`. Сценарии пользователя и примеры TSV — в руководствах RU/EN.

## English

Stage 11 adds clipboard TSV to **IN**, **INSERT**, and numeric-cell **Statistics**.
Source versions are **VS 0.2.15 / SSMS 0.6.14**; `release/` was not repackaged and
still contains stages 1–10 (VS 0.2.14 / SSMS 0.6.13).

All four adapters build with zero warnings/errors. **1728 tests** pass (1583
Core/Configuration/CLI + 145 golden), including **59 new scenarios**. All four
RU/EN × light/dark UI variants and four VSIX checks pass. Coverage includes TSV
quoting, limits, headers, conservative column types, null/empty values, ISO dates,
escaping, table-name rejection, 1001-row batching, three native profiles, statistics
precision/overflow and cancellation. Menu contracts contain **17 commands / 9
context placements**, IDs **0x0290–0x0292**, resource revision **4**.

The shared preview's first-line overlap and off-screen buttons were fixed and
covered at normal/minimum sizes. Standalone dialogs and the native preview were
visually inspected. Source-build evidence is separate from a release delivery.

Installed **VS2022 17.14.37710.0 x64**, extension **0.2.15**, confirmed IN at the
caret, INSERT replacing a selection with headers/escaped Unicode data, one Undo
for each, preview cancellation, and statistics (4 cells, 2 numeric, sum 3.5,
average 1.75). The standard installer was used first; after the preview fix,
the stopped IDE's adapter DLL was updated from the final VSIX. Installed adapter,
Core and Configuration hashes match that VSIX. Original clipboard contents were
restored, source SQL remained unchanged/saved and the test document was closed.
No SQL execution or connection was used.

VS2026/SSMS20/SSMS22 were not run for stage 11. No live grid, COM ResultsGrid,
HTML/CSV or schema/IDENTITY validation is claimed. Inputs are bounded TSV text;
headers/text mode are explicit. Insertion rejects changed document/snapshot/caret/
selection and multi-span rectangular selections. Statistics requires exact decimal
cells/final sum and rounds the average. There are no new CLI commands.

Logs, TRX, installed-host result and hash evidence are local under
`artifacts/stage11/`; UI captures are under `artifacts/settings-ui/`. They are
excluded from the commit. Reproduce with the solution build/test, settings smoke
and per-adapter VSIX checks; user steps are in both manuals.
