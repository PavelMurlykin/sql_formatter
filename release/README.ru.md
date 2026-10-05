# Установка T-SQL Formatter

Локальная сборка от 2026-10-05. Выберите пакет для своей IDE:

| IDE | Файл | Версия расширения |
| --- | --- | --- |
| Visual Studio 2022 17.14.x, x64 | `TSqlFormatter.VS2022.vsix` | 0.2.9 |
| Visual Studio 2026 18.x, x64 | `TSqlFormatter.VS2026.vsix` | 0.2.9 |
| SSMS 20.x, x86 | `TSqlFormatter.SSMS20.vsix` + `Install-SSMS20.ps1` | 0.6.8 |
| SSMS 22.x, x64 | `TSqlFormatter.SSMS22.vsix` | 0.6.8 |

Для готовых расширений .NET 10 SDK не нужен. Их целевая платформа — .NET Framework 4.7.2; зависимости форматирования включены в VSIX. VS 2022 старше 17.14 нужно обновить до ветки 17.14.

## Visual Studio 2022 и 2026

Закройте соответствующую Visual Studio, откройте её VSIX двойным щелчком, выберите нужный экземпляр IDE и завершите установку. Перезапустите IDE. Команды находятся в **Extensions → SQL Formatter**; откройте `.sql` и выберите **Format T-SQL Document**. SQL не выполняется. Правка отменяется одним Ctrl+Z; сохраните файл отдельно.

## SSMS 20

Закройте SSMS 20. Поместите VSIX и скрипт в одну папку. Откройте PowerShell **от имени администратора**, перейдите в эту папку и выполните:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SSMS20.ps1
```

При нестандартном расположении SSMS укажите путь к папке, содержащей `Ssms.exe`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SSMS20.ps1 -IdeDirectory "D:\Apps\SSMS20\Common7\IDE"
```

Скрипт проверяет поколение SSMS и содержимое пакета, устанавливает его в `Extensions\TSqlFormatter.Ssms20` и при повторной установке сохраняет предыдущую копию вне `Extensions`. Неизвестную существующую папку он не перезаписывает. SSMS 20 использует старую оболочку, поэтому стандартный VSIX Installer этот пакет не устанавливает: VSIX служит контейнером для скрипта.

Запустите SSMS 20, откройте `.sql` без подключения к серверу и выберите **SQL Formatter → T-SQL Formatter (SSMS): Format Document**.

## SSMS 22

Закройте SSMS 22. Запустите VSIX Installer из установленного SSMS 22 и передайте ему `TSqlFormatter.SSMS22.vsix`. Пример для стандартного пути и текущей папки `release`:

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" (Join-Path $PWD 'TSqlFormatter.SSMS22.vsix')
```

Выберите экземпляр SSMS 22 в установщике и после установки перезапустите его. Используйте верхнее меню **SQL Formatter**.

## Профили

В комплект включены нативные JSON v2:

- `ADIR_SQL_Main.json` — настройки, подготовленные по профилю SQL Prompt;
- `AV_Profile.json` и `Right-aligned-EPM-AWB2.json` — настройки по профилям SQL Complete, по 573 правила в каждом.

Откройте **SQL Formatter → Сохранённые профили…** (в Visual Studio сначала **Extensions**), нажмите **Импорт JSON…**, выберите файл, введите его имя и нажмите **Сохранить профиль**. При необходимости выберите **Использовать по умолчанию**, затем **OK**. Повторите импорт и сохранение отдельно для каждого профиля.

Ближайший `.tsqlformatter.json` имеет приоритет над выбранным профилем IDE. Для настроек проекта можно сохранить копию одного JSON под этим именем рядом с SQL-файлами, предварительно сохранив существующую конфигурацию. Исходные XML/style-файлы сторонних продуктов напрямую не импортируются. Конвертированные настройки имеют описанные в руководстве приближения и не обещают идентичный вывод SQL Prompt/SQL Complete.

[Результаты проверок](VALIDATION.md), [русское руководство](../docs/user-manual.ru.md), [English installation guide](README.en.md). `release-manifest.json` содержит SHA-256 установочных файлов и профилей; `profile-verification.json` — автоматическую проверку профилей. Дополнительный пакет CLI требует .NET 10 Runtime.
