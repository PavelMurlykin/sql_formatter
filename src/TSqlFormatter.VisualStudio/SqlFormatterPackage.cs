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
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using TSqlFormatter.Configuration;
using TSqlFormatter.IdeShared;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.History;
using TSqlFormatter.Core.Navigation;
using TSqlFormatter.Core.Parsing;
using TSqlFormatter.Core.Refactoring;
using TSqlFormatter.Core.Snippets;

namespace TSqlFormatter.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 2)]
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
    private EditorHistoryIntegration? historyIntegration;
    private readonly System.Collections.Generic.HashSet<uint> savesInProgress = new();
    private readonly SqlAnalysisMarkers analysisMarkers = new();
    private AnalysisReportWindow? analysisWindow;
    private NativeWindow? analysisWindowOwner;
    private SnippetInsertionSession? snippetSession;
    private ScriptOutlineWindow? outlineWindow;
    private NativeWindow? outlineWindowOwner;

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
        fullSettings.LegacyHistoryEnabledProvider = () => ((GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage))).KeepEditorHistory;
        if (runningDocumentTable != null)
            historyIntegration = new EditorHistoryIntegration(runningDocumentTable, editorAdapters, dte, fullSettings.ResolveEditorFeatures, HandleBeforeSave);
        fullSettings.FeaturesSaved = () => historyIntegration?.Refresh();
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            var format = new OleMenuCommand(ExecuteFormatDocument, new CommandID(CommandSet, 0x0200));
            format.BeforeQueryStatus += (_, _) =>
            {
                format.Text = SettingsAppearance.Text("Форматировать документ", "Format Document");
                format.Visible = format.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(format);
            var analyze = new OleMenuCommand(ExecuteAnalyzeDocument, new CommandID(CommandSet, 0x0210));
            analyze.BeforeQueryStatus += (_, _) =>
            {
                analyze.Text = SettingsAppearance.Text("Проанализировать документ", "Analyze Document");
                analyze.Visible = analyze.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(analyze);
            var clearAnalysis = new OleMenuCommand(ExecuteClearAnalysis, new CommandID(CommandSet, 0x0211));
            clearAnalysis.BeforeQueryStatus += (_, _) =>
            {
                clearAnalysis.Text = SettingsAppearance.Text("Очистить замечания анализа", "Clear Analysis Markers");
                clearAnalysis.Visible = clearAnalysis.Enabled = analysisMarkers.Count > 0;
            };
            commands.AddCommand(clearAnalysis);
            var insertSnippet = new OleMenuCommand(ExecuteInsertSnippet, new CommandID(CommandSet, 0x0220));
            insertSnippet.BeforeQueryStatus += (_, _) =>
            {
                insertSnippet.Text = SettingsAppearance.Text("Вставить сниппет", "Insert Snippet");
                insertSnippet.Visible = insertSnippet.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(insertSnippet);
            var rename = new OleMenuCommand(ExecuteRenameSymbol, new CommandID(CommandSet, 0x0230));
            rename.BeforeQueryStatus += (_, _) =>
            {
                rename.Text = SettingsAppearance.Text("Переименовать символ", "Rename Symbol");
                rename.Visible = rename.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(rename);
            var showOutline = new OleMenuCommand(ExecuteShowOutline, new CommandID(CommandSet, 0x0240));
            showOutline.BeforeQueryStatus += (_, _) =>
            {
                showOutline.Text = SettingsAppearance.Text("Структура скрипта", "Script Outline");
                showOutline.Visible = showOutline.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(showOutline);
            var goToDeclaration = new OleMenuCommand(ExecuteGoToDeclaration, new CommandID(CommandSet, 0x0241));
            goToDeclaration.BeforeQueryStatus += (_, _) =>
            {
                goToDeclaration.Text = SettingsAppearance.Text("Перейти к объявлению", "Go To Declaration");
                goToDeclaration.Visible = goToDeclaration.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(goToDeclaration);
            AddTransformCommand(commands, 0x0250, SqlTransformationKind.InsertToUpdate,
                "Преобразовать INSERT в UPDATE", "Convert INSERT to UPDATE");
            AddTransformCommand(commands, 0x0251, SqlTransformationKind.ValuesToUnionAll,
                "Преобразовать VALUES в UNION ALL", "Convert VALUES to UNION ALL");
            AddTransformCommand(commands, 0x0252, SqlTransformationKind.ExecWithReturnCode,
                "EXEC: перехватить код возврата", "EXEC: Capture Return Code");
            var generateCrud = new OleMenuCommand(ExecuteGenerateCrud, new CommandID(CommandSet, 0x0260));
            generateCrud.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                generateCrud.Text = SettingsAppearance.Text(
                    "Сгенерировать CRUD по CREATE TABLE", "Generate CRUD from CREATE TABLE");
                generateCrud.Visible = generateCrud.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
            };
            commands.AddCommand(generateCrud);
            var restoreHistory = new OleMenuCommand(ExecuteRestoreFromHistory, new CommandID(CommandSet, 0x0270));
            restoreHistory.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                restoreHistory.Text = SettingsAppearance.Text("Восстановить закрытый скрипт", "Restore Closed Script");
                restoreHistory.Visible = restoreHistory.Enabled = true;
            };
            commands.AddCommand(restoreHistory);
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
        if (disposing && historyIntegration != null)
        {
            JoinableTaskFactory.Run(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                historyIntegration.Dispose();
                historyIntegration = null;
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

    private void ExecuteAnalyzeDocument(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(AnalyzeDocumentAsync).FileAndForget("TSqlFormatter/AnalyzeDocument");
    }

    private void ExecuteClearAnalysis(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(ClearAnalysisAsync).FileAndForget("TSqlFormatter/ClearAnalysis");
    }

    private void ExecuteInsertSnippet(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(InsertSnippetAsync).FileAndForget("TSqlFormatter/InsertSnippet");
    }

    private void ExecuteRenameSymbol(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(RenameSymbolAsync).FileAndForget("TSqlFormatter/RenameSymbol");
    }

    private void ExecuteShowOutline(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(ShowOutlineAsync).FileAndForget("TSqlFormatter/ShowOutline");
    }

    private void ExecuteGoToDeclaration(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(GoToDeclarationAsync).FileAndForget("TSqlFormatter/GoToDeclaration");
    }

    private void AddTransformCommand(OleMenuCommandService commands, int id,
        SqlTransformationKind kind, string ruText, string enText)
    {
        var command = new OleMenuCommand((_, _) => ExecuteTransform(kind), new CommandID(CommandSet, id));
        command.BeforeQueryStatus += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            command.Text = SettingsAppearance.Text(ruText, enText);
            command.Visible = command.Enabled = ActiveSqlEditor.TryRead(textManager, out _, monitorSelection);
        };
        commands.AddCommand(command);
    }

    private void ExecuteTransform(SqlTransformationKind kind)
    {
        JoinableTaskFactory.RunAsync(() => TransformAsync(kind)).FileAndForget("TSqlFormatter/Transform");
    }

    private void ExecuteGenerateCrud(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(GenerateCrudAsync).FileAndForget("TSqlFormatter/GenerateCrud");
    }

    private void ExecuteRestoreFromHistory(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(RestoreFromHistoryAsync).FileAndForget("TSqlFormatter/RestoreFromHistory");
    }

    private async Task RestoreFromHistoryAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (dte is null)
        {
            NotifyFormat("The IDE automation service is unavailable.", true);
            return;
        }

        IReadOnlyList<EditorSnapshot> snapshots;
        try
        {
            var store = ((FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage))).ResolveEditorFeatures().CreateHistoryStore();
            snapshots = await Task.Run(() => { store.Prune(); return store.List(); }, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (snapshots.Count == 0)
        {
            NotifyFormat(SettingsAppearance.Text(
                "История пуста: снимки появляются после сохранения SQL-документов.",
                "The history is empty: snapshots appear when SQL documents are saved."), true);
            return;
        }

        using (var picker = new HistoryPickerWindow(snapshots))
        {
            if (ShowDialog(picker) != DialogResult.OK || picker.SelectedSnapshot is not { } snapshot)
            {
                return;
            }

            try
            {
                dte.ItemOperations.NewFile("General\\Text File");
                if (dte.ActiveDocument?.Object("TextDocument") is EnvDTE.TextDocument document)
                {
                    document.StartPoint.CreateEditPoint().Insert(snapshot.Content);
                    NotifyFormat(string.Format(SettingsAppearance.Text(
                        "Снимок «{0}» открыт в новом документе.", "Snapshot '{0}' opened in a new document."),
                        snapshot.DisplayName));
                }
            }
            catch (Exception ex)
            {
                NotifyFormat($"Restore failed: {ex.Message}", true);
            }
        }
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

    private async Task AnalyzeDocumentAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) || editorAdapters == null)
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

        string source = snapshot.GetText();
        var analysisDefaults = ((FullSettingsOptionsPage)GetDialogPage(typeof(FullSettingsOptionsPage))).ResolveEditorFeatures().Analysis;
        try
        {
            var analysis = await Task.Run(() => SqlDocumentAnalysis.Run(editor.Path, source, DisposalToken, analysisDefaults), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (analysis.Error is { } error)
            {
                analysisMarkers.Clear();
                CloseAnalysisWindow();
                NotifyFormat($"{error.Code}: {error.Message}", true);
                return;
            }

            if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
                !ReferenceEquals(current.Buffer, editor.Buffer))
            {
                NotifyFormat("The SQL buffer changed during analysis; markers were not applied.", true);
                return;
            }

            analysisMarkers.Apply(editor.Buffer, analysis);
            if (analysis.Diagnostics.Count == 0)
            {
                CloseAnalysisWindow();
                NotifyFormat(analysis.Enabled
                    ? SettingsAppearance.Text("Анализ: замечаний нет.", "Analysis finished: no findings.")
                    : SettingsAppearance.Text("Анализ отключён в настройках.", "Analysis is disabled in settings."));
                return;
            }

            NotifyFormat(string.Format(SettingsAppearance.Text(
                "Анализ: замечаний — {0}.", "Analysis finished: {0} finding(s)."), analysis.Diagnostics.Count));
            ShowAnalysisWindow(editor, analysis.Diagnostics);
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels analysis without applying markers.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Analysis failed: {ex.Message}", true);
        }
    }

    private async Task ClearAnalysisAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        analysisMarkers.Clear();
        CloseAnalysisWindow();
        NotifyFormat(SettingsAppearance.Text("Замечания анализа сняты.", "Analysis markers cleared."));
    }

    private void ShowAnalysisWindow(ActiveSqlEditor editor, IReadOnlyList<TSqlFormatter.Core.Analysis.AnalysisDiagnostic> diagnostics)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        CloseAnalysisWindow();
        var window = new AnalysisReportWindow(diagnostics);
        window.Navigate += diagnostic =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) &&
                ReferenceEquals(current.Buffer, editor.Buffer))
            {
                SqlEditorNavigation.GoTo(current.View, diagnostic.Line, diagnostic.Column);
            }
        };
        window.FormClosed += (_, _) =>
        {
            analysisWindow = null;
            analysisWindowOwner?.ReleaseHandle();
            analysisWindowOwner = null;
        };
        analysisWindow = window;
        try
        {
            if (GetService(typeof(SVsUIShell)) is IVsUIShell shell &&
                ErrorHandler.Succeeded(shell.GetDialogOwnerHwnd(out IntPtr owner)))
            {
                analysisWindowOwner = new NativeWindow();
                analysisWindowOwner.AssignHandle(owner);
                window.Show(analysisWindowOwner);
                return;
            }
        }
        catch (Exception)
        {
            // An ownerless modeless window still displays the findings.
        }

        window.Show();
    }

    private void CloseAnalysisWindow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (analysisWindow is not null && !analysisWindow.IsDisposed)
        {
            analysisWindow.Close();
        }

        analysisWindow = null;
        analysisWindowOwner?.ReleaseHandle();
        analysisWindowOwner = null;
    }

    private async Task GenerateCrudAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null || undoRegistry == null)
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

        string source = snapshot.GetText();
        var scope = new SqlTextSpan(0, source.Length);
        if (editor.TryCaptureSelection(editorAdapters, out var selectedSnapshot, out var selected) &&
            ReferenceEquals(selectedSnapshot, snapshot) && selected.Length > 0)
        {
            scope = new SqlTextSpan(selected.Start, selected.Length);
        }

        FormattingOptions defaults = CreateIdeOptions();
        TSqlFormatter.Core.Generation.SqlTableDefinition? table = null;
        string generated = string.Empty;
        try
        {
            var tableLocal = table;
            var generatedLocal = generated;
            await Task.Run(() =>
            {
                var parseResult = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, DisposalToken);
                tableLocal = new TSqlFormatter.Core.Generation.TableDefinitionReader().Read(parseResult, scope);
                if (tableLocal is not null)
                {
                    generatedLocal = TSqlFormatter.Core.Generation.CrudGenerator.Generate(tableLocal, defaults, DisposalToken);
                }
            }, DisposalToken);
            table = tableLocal;
            generated = generatedLocal;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"CRUD generation failed: {ex.Message}", true);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (table is null)
        {
            NotifyFormat(SettingsAppearance.Text(
                "CREATE TABLE не найден в документе или выделении.",
                "No CREATE TABLE was found in the document or selection."), true);
            return;
        }

        var before = source.Substring(table.StatementSpan.StartOffset, table.StatementSpan.Length);
        using (var preview = new TransformationPreviewWindow(
            SettingsAppearance.Text("Генерация CRUD — ", "Generate CRUD — ") + table.DisplayName,
            before, generated))
        {
            if (ShowDialog(preview) != DialogResult.OK)
            {
                return;
            }
        }

        var insertAt = table.StatementSpan.EndOffset;
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
            !ReferenceEquals(current.Buffer, editor.Buffer) ||
            !editor.TryApplyEdit(snapshot, insertAt, 0,
                Environment.NewLine + Environment.NewLine + generated.TrimEnd() + Environment.NewLine,
                "Generate T-SQL CRUD", editorAdapters, undoRegistry))
        {
            NotifyFormat("The SQL buffer changed during generation; no edit was applied.", true);
            return;
        }

        NotifyFormat(SettingsAppearance.Text(
            "CRUD сгенерирован после CREATE TABLE.", "CRUD templates were generated after the CREATE TABLE."));
    }

    private async Task TransformAsync(SqlTransformationKind kind)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var snapshot, out int caret) ||
            snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL caret is unavailable or the buffer exceeds 16 Mi characters.", true);
            return;
        }

        string source = snapshot.GetText();
        FormattingOptions defaults = CreateIdeOptions();
        SqlTransformationResult transformation;
        try
        {
            transformation = await Task.Run(
                () => SqlTransformer.Transform(kind, source, caret, defaults, DisposalToken), DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Transformation failed: {ex.Message}", true);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!transformation.Succeeded)
        {
            NotifyFormat(transformation.Error ?? "The transformation was not applied.", true);
            return;
        }

        var before = source.Substring(
            transformation.ReplacedSpan.StartOffset, transformation.ReplacedSpan.Length);
        using (var preview = new TransformationPreviewWindow(
            SettingsAppearance.Text("Преобразование SQL", "SQL Transformation"), before, transformation.NewStatementText))
        {
            if (ShowDialog(preview) != DialogResult.OK)
            {
                return;
            }
        }

        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current, monitorSelection) ||
            !ReferenceEquals(current.Buffer, editor.Buffer) ||
            !editor.TryApplyDocument(snapshot, transformation.NewDocumentText, editorAdapters, undoRegistry))
        {
            NotifyFormat("The SQL buffer changed during transformation; no edit was applied.", true);
            return;
        }

        NotifyFormat(SettingsAppearance.Text("Преобразование применено.", "Transformation applied."));
    }

    private async Task ShowOutlineAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null)
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

        string source = snapshot.GetText();
        ScriptOutlineNode? outline;
        try
        {
            outline = await Task.Run(() =>
            {
                var parseResult = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, DisposalToken);
                return new ScriptOutlineBuilder().Build(parseResult);
            }, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Script outline failed: {ex.Message}", true);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (outline is null)
        {
            NotifyFormat("SQL could not be parsed; the outline is unavailable.", true);
            return;
        }

        var lineMap = new SqlLineMap(source);
        CloseOutlineWindow();
        var window = new ScriptOutlineWindow(outline);
        window.Navigate += node =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ActiveSqlEditor.TryRead(textManager, out var current, monitorSelection) &&
                ReferenceEquals(current.Buffer, editor.Buffer))
            {
                var position = lineMap.GetLinePosition(node.Span.StartOffset);
                SqlEditorNavigation.GoTo(current.View, position.Line, position.Column);
            }
        };
        window.FormClosed += (_, _) =>
        {
            outlineWindow = null;
            outlineWindowOwner?.ReleaseHandle();
            outlineWindowOwner = null;
        };
        outlineWindow = window;
        ShowOwned(window, ref outlineWindowOwner);
    }

    private void ShowOwned(Form window, ref NativeWindow? owner)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (GetService(typeof(SVsUIShell)) is IVsUIShell shell &&
                ErrorHandler.Succeeded(shell.GetDialogOwnerHwnd(out IntPtr ownerHwnd)))
            {
                owner = new NativeWindow();
                owner.AssignHandle(ownerHwnd);
                window.Show(owner);
                return;
            }
        }
        catch (Exception)
        {
            // An ownerless modeless window still displays.
        }

        window.Show();
    }

    private async Task GoToDeclarationAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var snapshot, out int caret) ||
            snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL caret is unavailable or the buffer exceeds 16 Mi characters.", true);
            return;
        }

        string source = snapshot.GetText();
        SqlTextSpan? declaration;
        try
        {
            declaration = await Task.Run(() =>
            {
                var parseResult = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, DisposalToken);
                return new SymbolLocator().FindVariableDeclaration(parseResult, caret);
            }, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Go to declaration failed: {ex.Message}", true);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (declaration is null)
        {
            NotifyFormat(SettingsAppearance.Text(
                "Объявление переменной под курсором не найдено.",
                "No declaration found for the variable under the caret."), true);
            return;
        }

        if (!ActiveSqlEditor.TryRead(textManager, out var current, monitorSelection) ||
            !ReferenceEquals(current.Buffer, editor.Buffer))
        {
            NotifyFormat("The SQL buffer changed; navigation was not applied.", true);
            return;
        }

        var position = new SqlLineMap(source).GetLinePosition(declaration.Value.StartOffset);
        SqlEditorNavigation.GoTo(current.View, position.Line, position.Column);
        NotifyFormat(SettingsAppearance.Text("Переход к объявлению выполнен.", "Moved to the declaration."));
    }

    private void CloseOutlineWindow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (outlineWindow is not null && !outlineWindow.IsDisposed)
        {
            outlineWindow.Close();
        }

        outlineWindow = null;
        outlineWindowOwner?.ReleaseHandle();
        outlineWindowOwner = null;
    }

    private async Task RenameSymbolAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var snapshot, out int caret) ||
            snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL caret is unavailable or the buffer exceeds 16 Mi characters.", true);
            return;
        }

        string source = snapshot.GetText();
        RenameSymbol? symbol;
        try
        {
            symbol = await Task.Run(() =>
            {
                var parseResult = new ScriptDomSqlParser().Parse(source, SqlDialectVersion.Auto, DisposalToken);
                return new RenameAnalyzer().FindSymbol(parseResult, caret);
            }, DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            NotifyFormat($"Rename failed: {ex.Message}", true);
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (symbol is null)
        {
            NotifyFormat(SettingsAppearance.Text(
                "Под курсором нет локальной переменной или алиаса таблицы.",
                "No local variable or table alias is found under the caret."), true);
            return;
        }

        using (var prompt = new RenamePromptWindow(symbol.Name, symbol.Kind))
        {
            if (ShowDialog(prompt) != DialogResult.OK)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<TSqlFormatter.Core.Formatting.TextEdit> edits;
            try
            {
                edits = new RenameAnalyzer().Plan(symbol, prompt.NewName);
            }
            catch (ArgumentException exception)
            {
                NotifyFormat(exception.Message, true);
                return;
            }

            var lineMap = new SqlLineMap(source);
            var changes = edits
                .Select(edit =>
                {
                    var position = lineMap.GetLinePosition(edit.Span.StartOffset);
                    return ($"{position.Line},{position.Column}",
                        source.Substring(edit.Span.StartOffset, edit.Span.Length),
                        edit.NewText);
                })
                .ToArray();
            using (var preview = new RenamePreviewWindow(changes))
            {
                if (ShowDialog(preview) != DialogResult.OK)
                {
                    return;
                }
            }

            var spans = edits.Select(edit => new Microsoft.VisualStudio.Text.Span(
                edit.Span.StartOffset, edit.Span.Length)).ToArray();
            var replacements = edits.Select(edit => edit.NewText).ToArray();
            if (!editor.TryApplyEdits(snapshot, spans, replacements,
                    "Rename T-SQL Symbol", editorAdapters, undoRegistry))
            {
                NotifyFormat("The SQL buffer changed during rename; no edit was applied.", true);
                return;
            }

            NotifyFormat(string.Format(SettingsAppearance.Text(
                "Переименовано вхождений: {0}.", "Renamed occurrences: {0}."), edits.Count));
        }
    }

    private DialogResult ShowDialog(Form form)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (GetService(typeof(SVsUIShell)) is IVsUIShell shell &&
                ErrorHandler.Succeeded(shell.GetDialogOwnerHwnd(out IntPtr owner)))
            {
                var nativeOwner = new NativeWindow();
                nativeOwner.AssignHandle(owner);
                try
                {
                    return form.ShowDialog(nativeOwner);
                }
                finally
                {
                    nativeOwner.ReleaseHandle();
                }
            }
        }
        catch (Exception)
        {
            // An ownerless dialog still works.
        }

        return form.ShowDialog();
    }

    private async Task InsertSnippetAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor, monitorSelection) ||
            editorAdapters == null || undoRegistry == null)
        {
            NotifyFormat(SettingsAppearance.Text("Откройте файл .sql в редакторе.", "Open a .sql file in the text editor first."), true);
            return;
        }

        var textView = editorAdapters.GetWpfTextView(editor.View);
        var snapshot = editor.CaptureSnapshot(editorAdapters);
        if (textView == null || snapshot == null)
        {
            NotifyFormat("The SQL buffer is unavailable.", true);
            return;
        }

        if (snippetSession is { } session && session.IsCurrent(textView.TextBuffer.CurrentSnapshot))
        {
            var parameter = session.NextParameter();
            var sessionSnapshot = session.Snapshot;
            textView.Selection.Select(
                new SnapshotSpan(sessionSnapshot, parameter.StartOffset, parameter.Length), false);
            textView.Caret.MoveTo(new SnapshotPoint(sessionSnapshot, parameter.EndOffset));
            return;
        }

        snippetSession = null;
        System.Collections.Generic.IReadOnlyList<SqlSnippet> snippets;
        try
        {
            snippets = await Task.Run(() => new SnippetStore().Load(), DisposalToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (snippets.Count == 0)
        {
            NotifyFormat(SettingsAppearance.Text("Сниппеты недоступны.", "No snippets are available."), true);
            return;
        }

        using var picker = new SnippetPickerWindow(snippets);
        if (!ShowSnippetPicker(picker) || picker.SelectedSnippet is not { } snippet)
        {
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var caretSnapshot, out int caret) ||
            !ReferenceEquals(caretSnapshot, snapshot) || snapshot.Length > 16 * 1024 * 1024)
        {
            NotifyFormat("The SQL buffer changed during snippet insertion; no edit was applied.", true);
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

        var expanded = SnippetExpander.Expand(snippet.Body);
        if (!editor.TryApplyEdit(snapshot, start, length, expanded.Text,
                "Insert T-SQL Snippet", editorAdapters, undoRegistry))
        {
            NotifyFormat("The SQL buffer changed during snippet insertion; no edit was applied.", true);
            return;
        }

        var updated = textView.TextBuffer.CurrentSnapshot;
        if (expanded.Parameters.Count > 0)
        {
            var absolute = new SqlTextSpan[expanded.Parameters.Count];
            for (var index = 0; index < expanded.Parameters.Count; index++)
            {
                absolute[index] = new SqlTextSpan(
                    start + expanded.Parameters[index].StartOffset,
                    expanded.Parameters[index].Length);
            }

            snippetSession = new SnippetInsertionSession(updated, absolute);
            textView.Selection.Select(
                new SnapshotSpan(updated, absolute[0].StartOffset, absolute[0].Length), false);
            textView.Caret.MoveTo(new SnapshotPoint(updated, absolute[0].EndOffset));
        }

        NotifyFormat(string.Format(SettingsAppearance.Text(
            "Сниппет «{0}» вставлен.", "Snippet '{0}' inserted."), snippet.Name));
    }

    private bool ShowSnippetPicker(SnippetPickerWindow picker)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (GetService(typeof(SVsUIShell)) is IVsUIShell shell &&
                ErrorHandler.Succeeded(shell.GetDialogOwnerHwnd(out IntPtr owner)))
            {
                var nativeOwner = new NativeWindow();
                nativeOwner.AssignHandle(owner);
                try
                {
                    return picker.ShowDialog(nativeOwner) == DialogResult.OK;
                }
                finally
                {
                    nativeOwner.ReleaseHandle();
                }
            }
        }
        catch (Exception)
        {
            // An ownerless dialog still works.
        }

        return picker.ShowDialog() == DialogResult.OK;
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
