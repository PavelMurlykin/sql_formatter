using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using TSqlFormatter.Configuration;
using TSqlFormatter.IdeShared;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.Ssms;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(SsmsOptionsPage), "T-SQL Formatter (SSMS)", "General", 0, 0, true)]
[ProvideOptionPage(typeof(FullSettingsOptionsPage), "T-SQL Formatter (SSMS)", "All settings", 0, 0, true)]
[Guid(PackageGuid)]
public sealed class SsmsPackage : AsyncPackage
{
    public const string PackageGuid = "908068E6-40D9-4543-AB9F-1B952930F1E3";
    private static readonly Guid CommandSet = new("4EE4F956-58EC-490D-9DCA-D2D198570CEC");
    private IVsTextManager? textManager;
    private IVsMonitorSelection? monitorSelection;
    private EnvDTE.DTE? dte;
    private IVsEditorAdaptersFactoryService? editorAdapters;
    private ITextUndoHistoryRegistry? undoRegistry;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        textManager = await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        monitorSelection = await GetServiceAsync(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
        dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        IdeHostIntegration.SetLanguage(dte);
        var components = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        editorAdapters = components?.GetService<IVsEditorAdaptersFactoryService>();
        undoRegistry = components?.GetService<ITextUndoHistoryRegistry>();
        var fullSettings = (FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage));
        fullSettings.LegacyOptionsProvider = () => ((SsmsOptionsPage)GetDialogPage(typeof(SsmsOptionsPage))).CreateOptions();
        fullSettings.SqlPathProvider = () => ActiveQueryEditor.TryRead(textManager, out var editor, monitorSelection) ? editor.Path : null;
        ActivityLog.LogInformation("T-SQL Formatter SSMS", "SSMS spike package initialized.");
        fullSettings.ShortcutProvider = () => IdeHostIntegration.Shortcut(dte, CommandSet);
        fullSettings.ShortcutApply = value => IdeHostIntegration.SetShortcut(dte, CommandSet, value);
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            var format = new OleMenuCommand(ExecuteFormatDocument, new CommandID(CommandSet, 0x0200));
            format.BeforeQueryStatus += (_, _) =>
            {
                format.Text = SettingsAppearance.Text("Форматировать документ", "Format Document");
                format.Visible = format.Enabled = ActiveQueryEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(format);
            var settings = new OleMenuCommand((_, _) => ShowSettings(false), new CommandID(CommandSet, 0x0400));
            settings.BeforeQueryStatus += (_, _) => settings.Text = SettingsAppearance.Text("Настройки…", "Settings…");
            commands.AddCommand(settings);
        }
    }

    private void ShowSettings(bool profiles)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var shell = GetService(typeof(SVsUIShell)) as IVsUIShell
                ?? throw new InvalidOperationException(SettingsAppearance.Text("Окно IDE недоступно.", "IDE window is unavailable."));
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(shell.GetDialogOwnerHwnd(out IntPtr owner));
            ((FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage))).ShowEditor(profiles, owner);
        }
        catch (Exception ex) { ShowMessage(SettingsAppearance.Text("Не удалось открыть настройки: ", "Unable to open settings: ") + ex.Message, true); }
    }

    private void ExecuteFormatDocument(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(() => FormatAsync(FormatScope.Document)).FileAndForget("TSqlFormatter.Ssms/FormatDocument");
    }

    private void ExecuteFormatSelection(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(() => FormatAsync(FormatScope.Selection)).FileAndForget("TSqlFormatter.Ssms/FormatSelection");
    }

    private void ExecuteFormatStatement(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(() => FormatAsync(FormatScope.Statement)).FileAndForget("TSqlFormatter.Ssms/FormatStatement");
    }

    private async Task FormatAsync(FormatScope scope)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveQueryEditor.TryRead(textManager, out var editor, monitorSelection) || editorAdapters == null || undoRegistry == null)
        {
            ShowMessage("Open a .sql file in the SSMS query editor first.", true);
            return;
        }

        Microsoft.VisualStudio.Text.ITextSnapshot? snapshot;
        SqlTextSpan? target = null;
        if (scope == FormatScope.Selection)
        {
            if (!editor.TryCaptureSelection(editorAdapters, out var selectedSnapshot, out var selected))
            {
                ShowMessage("Select SQL text in a .sql query first.", true);
                return;
            }
            snapshot = selectedSnapshot;
            target = new SqlTextSpan(selected.Start, selected.Length);
        }
        else if (scope == FormatScope.Statement)
        {
            if (!editor.TryCaptureCaret(editorAdapters, out var caretSnapshot, out int caret))
            {
                ShowMessage("The SQL caret is unavailable.", true);
                return;
            }
            snapshot = caretSnapshot;
            target = new SqlTextSpan(caret, 0);
        }
        else
        {
            snapshot = editor.CaptureSnapshot(editorAdapters);
            if (editor.TryCaptureSelection(editorAdapters, out var selectedSnapshot, out var selected) && ReferenceEquals(selectedSnapshot, snapshot))
            { target = new SqlTextSpan(selected.Start, selected.Length); scope = FormatScope.Selection; }
        }
        if (snapshot == null || snapshot.Length > 16 * 1024 * 1024)
        {
            ShowMessage("The SQL buffer is unavailable or exceeds 16 Mi characters.", true);
            return;
        }

        try
        {
            string source = snapshot.GetText();
            FormattingOptions defaults = ((SsmsOptionsPage)GetDialogPage(typeof(SsmsOptionsPage))).CreateOptions();
            defaults = ((FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage))).ResolveOptions(defaults);
            var configured = await Task.Run(() =>
            {
                var config = new SqlFormatterConfigurationResolver().ResolveForSqlFile(editor.Path, defaults);
                if (!config.Succeeded) return (Result: (FormatResult?)null, Error: config.Diagnostics[0]);
                var result = scope == FormatScope.Statement
                    ? new ScriptDomSqlFormatter().Format(source, config.Options!, new FormatRequest(scope, target), DisposalToken)
                    : new EditorSqlFormatter().Format(source, config.Options!, target, DisposalToken);
                return (Result: result, Error: (FormatterDiagnostic?)null);
            }, DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (configured.Error != null)
                ShowMessage($"{configured.Error.Code}: {configured.Error.Message}", true);
            else if (configured.Result is not { } result || !result.ParseSucceeded ||
                     System.Linq.Enumerable.Any(result.Diagnostics, d => d.Severity == FormatterDiagnosticSeverity.Error))
                ShowMessage("SQL could not be parsed; no edit was applied.", true);
            else if (!result.Changed)
            {
                var diagnostic = System.Linq.Enumerable.FirstOrDefault(result.Diagnostics);
                ShowMessage(diagnostic == null ? "The SQL target is already formatted."
                    : $"{diagnostic.Code}: {diagnostic.Message}", diagnostic != null);
            }
            else if (!ActiveQueryEditor.TryRead(textManager, out var current, monitorSelection) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     (scope == FormatScope.Document
                         ? !editor.TryApplyDocument(snapshot, result.Text, editorAdapters, undoRegistry)
                         : result.Edits.Count != 1 || !editor.TryApplyEdit(snapshot,
                             result.Edits[0].Span.StartOffset, result.Edits[0].Span.Length,
                             result.Edits[0].NewText, scope == FormatScope.Selection
                                 ? "Format T-SQL Selection" : "Format T-SQL Statement", editorAdapters, undoRegistry)))
                ShowMessage("The SQL buffer changed during formatting; no edit was applied.", true);
            else
                ShowMessage("SQL formatted. Use Undo to revert.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowMessage($"Formatting failed: {ex.Message}", true);
        }
    }

    private void ShowMessage(string message, bool error = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ActivityLog.LogInformation("T-SQL Formatter SSMS", message);
        VsShellUtilities.ShowMessageBox(this, message, "T-SQL Formatter (SSMS)",
            error ? OLEMSGICON.OLEMSGICON_CRITICAL : OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
