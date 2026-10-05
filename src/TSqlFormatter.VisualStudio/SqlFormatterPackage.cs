using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using TSqlFormatter.Configuration;
using TSqlFormatter.IdeShared;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(GeneralOptionsPage), "T-SQL Formatter", "General", 0, 0, true)]
[ProvideOptionPage(typeof(FullSettingsOptionsPage), "T-SQL Formatter", "All settings", 0, 0, true)]
[ProvideOptionPage(typeof(PreviewOptionsPage), "T-SQL Formatter", "SQL Preview", 0, 0, true)]
[ProvideOptionPage(typeof(SelectOptionsPage), "T-SQL Formatter", "SELECT", 0, 0, true)]
[ProvideOptionPage(typeof(JoinOptionsPage), "T-SQL Formatter", "JOIN", 0, 0, true)]
[ProvideOptionPage(typeof(WhereOptionsPage), "T-SQL Formatter", "WHERE", 0, 0, true)]
[ProvideOptionPage(typeof(ProfileOptionsPage), "T-SQL Formatter", "Profile", 0, 0, true)]
[Guid(PackageGuid)]
public sealed class SqlFormatterPackage : AsyncPackage
{
    public const string PackageGuid = "C8BAF105-2C0F-484C-93DB-DC653596796B";
    private static readonly Guid CommandSet = new("164528C8-0EC5-4EEC-8380-7C17745D9701");
    private static readonly Guid OutputPaneGuid = new("AB9C0A91-8A8C-4413-9B11-94F73D59DE7D");
    private IVsTextManager? textManager;
    private IVsMonitorSelection? monitorSelection;
    private EnvDTE.DTE? dte;
    private IVsEditorAdaptersFactoryService? editorAdapters;
    private ITextUndoHistoryRegistry? undoRegistry;
    private IVsStatusbar? statusbar;
    private IVsOutputWindow? outputWindow;
    private IVsRunningDocumentTable? runningDocumentTable;
    private uint runningDocumentTableCookie;
    private readonly System.Collections.Generic.HashSet<uint> savesInProgress = new();

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        textManager = await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        monitorSelection = await GetServiceAsync(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
        dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        IdeHostIntegration.SetLanguage(dte);
        statusbar = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
        outputWindow = await GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        runningDocumentTable = await GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
        if (runningDocumentTable != null)
            ErrorHandler.ThrowOnFailure(runningDocumentTable.AdviseRunningDocTableEvents(
                new SaveEventSink(HandleBeforeSave), out runningDocumentTableCookie));
        var components = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        editorAdapters = components?.GetService<IVsEditorAdaptersFactoryService>();
        undoRegistry = components?.GetService<ITextUndoHistoryRegistry>();
        ((PreviewOptionsPage)GetDialogPage(typeof(PreviewOptionsPage))).OptionsProvider =
            CreateIdeOptions;
        var fullSettings = (FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage));
        fullSettings.LegacyOptionsProvider = CreateLegacyOptions;
        fullSettings.SqlPathProvider = () => ActiveSqlEditor.TryRead(textManager, out var editor, monitorSelection) ? editor.Path : null;
        fullSettings.ShortcutProvider = () => IdeHostIntegration.Shortcut(dte, CommandSet);
        fullSettings.ShortcutApply = value => IdeHostIntegration.SetShortcut(dte, CommandSet, value);
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            var format = new OleMenuCommand(ExecuteFormatDocument, new CommandID(CommandSet, 0x0200));
            format.BeforeQueryStatus += (_, _) =>
            {
                format.Text = SettingsAppearance.Text("Форматировать документ", "Format Document");
                format.Visible = format.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
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
            ErrorHandler.ThrowOnFailure(shell.GetDialogOwnerHwnd(out IntPtr owner));
            ((FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage))).ShowEditor(profiles, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(SettingsAppearance.Text("Не удалось открыть настройки: ", "Unable to open settings: ") + ex.Message, "SQL Formatter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && runningDocumentTable != null && runningDocumentTableCookie != 0)
        {
            JoinableTaskFactory.Run(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                runningDocumentTable.UnadviseRunningDocTableEvents(runningDocumentTableCookie);
                runningDocumentTableCookie = 0;
            });
        }

        base.Dispose(disposing);
    }

    private int HandleBeforeSave(uint docCookie)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (savesInProgress.Contains(docCookie)) return VSConstants.S_OK;

        try
        {
            var automation = (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));
            var mode = automation.FormatOnSave;
            if (mode == SqlSaveFormattingMode.Off || runningDocumentTable == null ||
                editorAdapters == null || undoRegistry == null)
                return VSConstants.S_OK;

            int hr = runningDocumentTable.GetDocumentInfo(docCookie, out _, out _, out _,
                out string moniker, out _, out _, out IntPtr docData);
            if (docData != IntPtr.Zero) Marshal.Release(docData);
            if (ErrorHandler.Failed(hr) ||
                !ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
                !string.Equals(moniker, editor.Path, StringComparison.OrdinalIgnoreCase) ||
                !new SqlSaveFormattingPolicy().ShouldFormat(editor.Path, mode, automation.SaveExclusions))
                return VSConstants.S_OK;

            var snapshot = editor.CaptureSnapshot(editorAdapters);
            if (snapshot == null || !SqlAutomationLimits.CanProcess(snapshot.Length))
            {
                NotifyFormat("Save formatting skipped: SQL buffer is unavailable or exceeds 8 Ki characters.", true);
                return VSConstants.S_OK;
            }

            savesInProgress.Add(docCookie);
            var defaults = CreateIdeOptions();
            string source = snapshot.GetText();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(DisposalToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var configured = JoinableTaskFactory.Run(async () => await Task.Run(() =>
                FormatConfigured(editor.Path, source, new FormatRequest(), defaults, timeout.Token), timeout.Token));
            if (configured.Error != null)
                NotifyFormat($"Save formatting skipped: {configured.Error.Code}: {configured.Error.Message}", true);
            else if (configured.Result is { } result)
            {
                var error = result.Diagnostics.FirstOrDefault(d => d.Severity == FormatterDiagnosticSeverity.Error);
                if (error != null)
                    NotifyFormat($"Save formatting skipped: {error.Code}: {error.Message}", true);
                else if (result.Changed)
                {
                    if (ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) &&
                        ReferenceEquals(current.Buffer, editor.Buffer) &&
                        editor.TryApplyDocument(snapshot, result.Text, editorAdapters, undoRegistry))
                        NotifyFormat("SQL formatted before save.");
                    else
                        NotifyFormat("Save formatting skipped: SQL buffer changed.", true);
                }
            }
        }
        catch (OperationCanceledException) when (!DisposalToken.IsCancellationRequested)
        {
            NotifyFormat("Save formatting timed out after 2 seconds; saving unformatted SQL.", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            NotifyFormat($"Save formatting failed: {ex.Message}", true);
        }
        finally
        {
            savesInProgress.Remove(docCookie);
        }

        return VSConstants.S_OK;
    }

    private void ExecuteFormatDocument(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(FormatDocumentAsync).FileAndForget("TSqlFormatter/FormatDocument");
    }

    private void ExecuteFormatSelection(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(FormatSelectionAsync).FileAndForget("TSqlFormatter/FormatSelection");
    }

    private void ExecuteFormatStatement(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(FormatStatementAsync).FileAndForget("TSqlFormatter/FormatStatement");
    }

    private void ExecuteImportProfile(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(ImportProfileAsync).FileAndForget("TSqlFormatter/ImportProfile");
    }

    private void ExecuteExportProfile(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(ExportProfileAsync).FileAndForget("TSqlFormatter/ExportProfile");
    }

    private void ExecutePasteFormatted(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(PasteFormattedAsync).FileAndForget("TSqlFormatter/PasteFormatted");
    }

    private async Task PasteFormattedAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat("Open a .sql file in the text editor before pasting.", true);
            return;
        }

        string clipboard;
        try { clipboard = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
        catch (Exception ex)
        {
            NotifyFormat($"Cannot read text from the clipboard: {ex.Message}", true);
            return;
        }
        if (string.IsNullOrWhiteSpace(clipboard) || !SqlAutomationLimits.CanProcess(clipboard.Length))
        {
            NotifyFormat("Clipboard must contain SQL text of at most 8 Ki characters.", true);
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var snapshot, out int caret) ||
            snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("SQL buffer is unavailable or exceeds 16 Mi characters.", true);
            return;
        }

        int start = caret;
        int length = 0;
        if (editor.TryCaptureSelection(editorAdapters, out var selectedSnapshot, out var selected) &&
            ReferenceEquals(selectedSnapshot, snapshot))
        {
            start = selected.Start;
            length = selected.Length;
        }

        try
        {
            FormattingOptions defaults = CreateIdeOptions();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(DisposalToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var configured = await Task.Run(() => FormatConfigured(
                editor.Path, clipboard, new FormatRequest(), defaults, timeout.Token), timeout.Token);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (configured.Error != null)
            {
                NotifyFormat($"{configured.Error.Code}: {configured.Error.Message}", true);
                return;
            }

            FormatResult result = configured.Result!;
            var error = result.Diagnostics.FirstOrDefault(d => d.Severity == FormatterDiagnosticSeverity.Error);
            if (error != null || !result.ParseSucceeded)
            {
                NotifyFormat(error == null ? "Clipboard SQL could not be parsed."
                    : $"{error.Code}: {error.Message}", true);
                return;
            }

            if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
                !ReferenceEquals(current.Buffer, editor.Buffer) ||
                !editor.TryApplyEdit(snapshot, start, length, result.Text,
                    "Paste formatted T-SQL", editorAdapters, undoRegistry))
            {
                NotifyFormat("SQL buffer changed during paste formatting; no edit was applied.", true);
                return;
            }

            NotifyFormat("Formatted clipboard SQL pasted.");
        }
        catch (OperationCanceledException) when (!DisposalToken.IsCancellationRequested)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat("Formatted paste timed out after 2 seconds; no edit was applied.", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Formatted paste failed: {ex.Message}", true);
        }
    }

    private async Task ImportProfileAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        using var dialog = new OpenFileDialog
        {
            Title = "Import T-SQL formatter profile",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var imported = await Task.Run(() =>
                new SqlFormatterProfileExchange().Import(dialog.FileName), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (!imported.Succeeded)
            {
                var diagnostic = imported.Diagnostics[0];
                NotifyFormat($"{diagnostic.Code}: {diagnostic.Message}", true);
                return;
            }

            var options = imported.Options!;
            if (options.General.MaxLineWidth > 4096 || options.Indent.Size > 32)
            {
                NotifyFormat("Profile exceeds the IDE limits (line length 4096, indent size 32).", true);
                return;
            }

            ApplyImportedProfile(options);
            NotifyFormat("Profile imported into IDE settings; the Default profile is now selected.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Profile import failed: {ex.Message}", true);
        }
    }

    private async Task ExportProfileAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        FormattingOptions options;
        try { options = CreateIdeOptions(); }
        catch (Exception ex)
        {
            NotifyFormat($"Cannot export IDE options: {ex.Message}", true);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export T-SQL formatter profile",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "tsqlformatter-profile.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            await Task.Run(() => new SqlFormatterProfileExchange().Export(dialog.FileName, options), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            NotifyFormat($"Profile exported: {dialog.FileName}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Profile export failed: {ex.Message}", true);
        }
    }

    private void ApplyImportedProfile(FormattingOptions options)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var general = (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));
        var select = (SelectOptionsPage)GetDialogPage(typeof(SelectOptionsPage));
        var joins = (JoinOptionsPage)GetDialogPage(typeof(JoinOptionsPage));
        var where = (WhereOptionsPage)GetDialogPage(typeof(WhereOptionsPage));
        var profile = (ProfileOptionsPage)GetDialogPage(typeof(ProfileOptionsPage));

        general.MaxLineLength = options.General.MaxLineWidth;
        general.LineEnding = options.General.LineEnding;
        general.FinalNewLine = options.General.FinalNewline;
        general.IndentSize = options.Indent.Size;
        general.UseTabs = options.Indent.UseTabs;
        general.KeywordCase = options.Keywords.Case;
        select.Columns = options.Select.ColumnLayout;
        select.GroupByItems = options.Clauses.GroupByLayout;
        select.OrderByItems = options.Clauses.OrderByLayout;
        joins.ClauseNewLine = options.Joins.ClauseNewLine;
        joins.ConditionNewLine = options.Joins.ConditionNewLine;
        where.ConditionNewLine = options.Where.ConditionNewLine;
        where.BooleanOperatorNewLine = options.Where.BooleanOperatorNewLine;
        profile.Profile = IdeProfileId.Default;

        general.SaveSettingsToStorage();
        select.SaveSettingsToStorage();
        joins.SaveSettingsToStorage();
        where.SaveSettingsToStorage();
        profile.SaveSettingsToStorage();
        var fullSettings = (FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage));
        fullSettings.ImportOptions(options);
        fullSettings.SaveSettingsToStorage();
    }

    private async Task FormatStatementAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) || editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var snapshot, out int caret))
        {
            NotifyFormat("The SQL caret is unavailable.", true);
            return;
        }

        if (snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL buffer exceeds 16 Mi characters.", true);
            return;
        }

        string source = snapshot.GetText();
        try
        {
            FormattingOptions defaults = CreateIdeOptions();
            var configured = await Task.Run(() => FormatConfigured(
                editor.Path, source, new FormatRequest(FormatScope.Statement,
                    new SqlTextSpan(caret, 0)), defaults, DisposalToken), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (configured.Error != null)
            {
                NotifyFormat($"{configured.Error.Code}: {configured.Error.Message}", true);
                return;
            }

            FormatResult result = configured.Result!;
            if (result.Diagnostics.Count > 0)
            {
                var diagnostic = result.Diagnostics[0];
                NotifyFormat($"{diagnostic.Code}: {diagnostic.Message}", true);
            }
            else if (!result.Changed)
            {
                NotifyFormat("The SQL statement is already formatted.");
            }
            else if (result.Edits.Count != 1 ||
                     !ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     !editor.TryApplyEdit(snapshot, result.Edits[0].Span.StartOffset,
                         result.Edits[0].Span.Length, result.Edits[0].NewText,
                         "Format T-SQL Statement", editorAdapters, undoRegistry))
            {
                NotifyFormat("The SQL buffer changed during formatting; no edit was applied.", true);
            }
            else
            {
                NotifyFormat("SQL statement formatted.");
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels formatting without applying an edit.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Statement formatting failed: {ex.Message}", true);
        }
    }

    private async Task FormatSelectionAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) || editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        if (!editor.TryCaptureSelection(editorAdapters, out var snapshot, out var selected))
        {
            NotifyFormat("Select SQL text in a .sql document first.", true);
            return;
        }

        if (snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL buffer exceeds 16 Mi characters.", true);
            return;
        }

        string source = snapshot.GetText();
        try
        {
            FormattingOptions defaults = CreateIdeOptions();
            var span = new SqlTextSpan(selected.Start, selected.Length);
            var configured = await Task.Run(() => FormatConfigured(
                editor.Path, source, new FormatRequest(FormatScope.Selection, span), defaults, DisposalToken), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (configured.Error != null)
            {
                NotifyFormat($"{configured.Error.Code}: {configured.Error.Message}", true);
                return;
            }

            FormatResult result = configured.Result!;
            if (result.Diagnostics.Count > 0)
            {
                var diagnostic = result.Diagnostics[0];
                NotifyFormat($"{diagnostic.Code}: {diagnostic.Message}", true);
            }
            else if (!result.Changed)
            {
                NotifyFormat("The selected SQL statement is already formatted.");
            }
            else if (result.Edits.Count != 1 ||
                     !ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     !editor.TryApplyEdit(snapshot, result.Edits[0].Span.StartOffset,
                         result.Edits[0].Span.Length, result.Edits[0].NewText,
                         "Format T-SQL Selection", editorAdapters, undoRegistry))
            {
                NotifyFormat("The SQL buffer changed during formatting; no edit was applied.", true);
            }
            else
            {
                NotifyFormat("Selected SQL statement formatted.");
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels formatting without applying an edit.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Selection formatting failed: {ex.Message}", true);
        }
    }

    private async Task FormatDocumentAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) || editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        var snapshot = editor.CaptureSnapshot(editorAdapters);
        if (snapshot == null || snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL buffer is unavailable or exceeds 16 Mi characters.", true);
            return;
        }

        Microsoft.VisualStudio.Text.Span? selection = null;
        if (editor.TryCaptureSelection(editorAdapters, out var selectedSnapshot, out var selected) && ReferenceEquals(selectedSnapshot, snapshot)) selection = selected;
        string source = snapshot.GetText();
        try
        {
            FormattingOptions defaults = CreateIdeOptions();
            var configured = await Task.Run(() => FormatConfigured(
                editor.Path, source, new FormatRequest(), defaults, DisposalToken,
                selection is { } span ? new SqlTextSpan(span.Start, span.Length) : (SqlTextSpan?)null), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (configured.Error != null)
            {
                NotifyFormat($"{configured.Error.Code}: {configured.Error.Message}", true);
                return;
            }

            FormatResult result = configured.Result!;
            if (!result.ParseSucceeded || result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error))
            {
                var error = result.Diagnostics.FirstOrDefault();
                NotifyFormat(error == null ? "SQL could not be parsed." : $"{error.Code}: {error.Message}", true);
            }
            else if (!result.Changed)
            {
                NotifyFormat(result.Diagnostics.FirstOrDefault()?.Message ?? SettingsAppearance.Text("SQL уже отформатирован.", "SQL is already formatted."));
            }
            else if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     !(selection is null ? editor.TryApplyDocument(snapshot, result.Text, editorAdapters, undoRegistry)
                         : result.Edits.Count == 1 && editor.TryApplyEdit(snapshot, result.Edits[0].Span.StartOffset,
                             result.Edits[0].Span.Length, result.Edits[0].NewText, "Format T-SQL Selection", editorAdapters, undoRegistry)))
            {
                NotifyFormat("The SQL buffer changed during formatting; no edit was applied.", true);
            }
            else
            {
                NotifyFormat(SettingsAppearance.Text("SQL отформатирован.", "SQL formatted."));
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels formatting without applying an edit.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Formatting failed: {ex.Message}", true);
        }
    }

    private static (FormatResult? Result, FormatterDiagnostic? Error) FormatConfigured(
        string filePath, string source, FormatRequest request, FormattingOptions defaults,
        CancellationToken cancellationToken, SqlTextSpan? editorSelection = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = new SqlFormatterConfigurationResolver().ResolveForSqlFile(filePath, defaults);
        cancellationToken.ThrowIfCancellationRequested();
        if (!config.Succeeded)
        {
            return (null, config.Diagnostics.First());
        }

        return (editorSelection is not null
            ? new EditorSqlFormatter().Format(source, config.Options!, editorSelection, cancellationToken)
            : new ScriptDomSqlFormatter().Format(source, config.Options!, request, cancellationToken), null);
    }

    private FormattingOptions CreateIdeOptions()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var fullSettings = (FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage));
        return fullSettings.ResolveOptions(CreateLegacyOptions());
    }

    private FormattingOptions CreateLegacyOptions()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var profilePage = (ProfileOptionsPage)GetDialogPage(typeof(ProfileOptionsPage));
        if (profilePage.Profile != IdeProfileId.Default)
        {
            if (!new FormattingProfileCatalog().TryGet(profilePage.Profile.ToString(), out var profile)
                || profile == null)
                throw new InvalidOperationException($"Unknown IDE formatting profile '{profilePage.Profile}'.");
            return profile.Options;
        }

        var general = (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));
        var select = (SelectOptionsPage)GetDialogPage(typeof(SelectOptionsPage));
        var joins = (JoinOptionsPage)GetDialogPage(typeof(JoinOptionsPage));
        var where = (WhereOptionsPage)GetDialogPage(typeof(WhereOptionsPage));
        return general.CreateOptions().With(
            select: new SelectOptions(select.Columns),
            clauses: new QueryClauseOptions(select.GroupByItems, select.OrderByItems),
            joins: new JoinOptions(joins.ClauseNewLine, joins.ConditionNewLine),
            where: new WhereOptions(where.ConditionNewLine, where.BooleanOperatorNewLine));
    }

    private void NotifyFormat(string message, bool isError = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string text = $"T-SQL Formatter: {(isError ? "Error: " : string.Empty)}{message}";
        statusbar?.SetText(text);
        if (outputWindow == null) return;

        Guid paneId = OutputPaneGuid;
        if (ErrorHandler.Failed(outputWindow.GetPane(ref paneId, out IVsOutputWindowPane pane)) || pane == null)
        {
            outputWindow.CreatePane(ref paneId, "T-SQL Formatter", 1, 1);
            if (ErrorHandler.Failed(outputWindow.GetPane(ref paneId, out pane)) || pane == null)
                return;
        }

        pane.OutputStringThreadSafe(text + Environment.NewLine);
        if (isError) pane.Activate();
    }
}
