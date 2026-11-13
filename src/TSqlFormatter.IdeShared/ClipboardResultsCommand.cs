using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.QueryResults;

namespace TSqlFormatter.IdeShared;

/// <summary>UI-thread callbacks capture one document, text version and selection/caret.</summary>
internal sealed class ClipboardEditTarget
{
    internal ClipboardEditTarget(string path, string before, Func<bool> isCurrent, Func<string, bool> apply)
    { Path = path; Before = before; IsCurrent = isCurrent; Apply = apply; }
    internal string Path { get; }
    internal string Before { get; }
    internal Func<bool> IsCurrent { get; }
    internal Func<string, bool> Apply { get; }
}

internal static class ClipboardResultsCommand
{
    internal static async Task RunAsync(ClipboardResultKind kind, ClipboardEditTarget? target,
        FormattingOptions defaults, Func<Form, DialogResult> showDialog, SettingsAppearance appearance,
        Action<string, bool> notify, CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        if (kind != ClipboardResultKind.Statistics && target == null)
        {
            notify(T("Откройте SQL-документ с доступным курсором/выделением (до 16 Ми символов).",
                "Open a SQL document with an available caret/selection (up to 16 Mi characters)."), true); return;
        }
        try
        {
            string clipboard;
            try { clipboard = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch (ExternalException) { notify(T("Буфер обмена недоступен. Повторите команду.", "Clipboard is unavailable. Run the command again."), true); return; }
            var table = await Task.Run(() => TsvTable.Parse(clipboard, token), token);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            if (target != null && !target.IsCurrent()) { Stale(notify); return; }
            using var optionsWindow = new QueryResultsOptionsWindow(kind, table.Rows.Count, table.ColumnCount);
            optionsWindow.ApplyAppearance(appearance);
            if (showDialog(optionsWindow) != DialogResult.OK) return;
            var options = optionsWindow.Options; string name = optionsWindow.TableName;
            if (kind == ClipboardResultKind.Statistics)
            {
                var statistics = await Task.Run(() => TsvStatistics.Calculate(table, options.FirstRowHeaders, token), token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                using var window = new QueryResultsStatisticsWindow(statistics); appearance.Apply(window); showDialog(window); return;
            }
            string sql = await Task.Run(() =>
            {
                var config = new SqlFormatterConfigurationResolver().ResolveForSqlFile(target!.Path, defaults);
                if (!config.Succeeded) throw new ArgumentException(config.Diagnostics[0].Message);
                return kind == ClipboardResultKind.In ? QueryResultSql.CreateIn(table, options, config.Options!, token) :
                    QueryResultSql.CreateInsert(table, name, options, config.Options!, token);
            }, token);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            if (!target!.IsCurrent()) { Stale(notify); return; }
            using var preview = new TransformationPreviewWindow(QueryResultsOptionsWindow.Title(kind), target.Before, sql);
            appearance.Apply(preview);
            if (showDialog(preview) != DialogResult.OK) return;
            if (!target.IsCurrent() || !target.Apply(sql)) { Stale(notify); return; }
            notify(T("Данные из буфера вставлены. Правка отменяется одним Undo.", "Clipboard data inserted. One Undo reverts the edit."), false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            notify(T("Операция не выполнена: ", "Operation not completed: ") +
                (ex is ResultDataException data ? QueryResultsText.Error(data) : ex.Message), true);
        }
    }
    private static void Stale(Action<string, bool> notify) => notify(T(
        "Документ, текст или курсор/выделение изменились. Повторите команду; правки не внесены.",
        "The document, text or caret/selection changed. Run the command again; no edit was applied."), true);
    private static string T(string ru, string en) => SettingsAppearance.Text(ru, en);
}
