using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using TSqlFormatter.Core.Formatting;
using TSqlFormatter.Core.Parsing;

namespace TSqlFormatter.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuid)]
public sealed class SqlFormatterPackage : AsyncPackage
{
    public const string PackageGuid = "C8BAF105-2C0F-484C-93DB-DC653596796B";
    private static readonly Guid CommandSet = new("164528C8-0EC5-4EEC-8380-7C17745D9701");
    private IVsTextManager? textManager;
    private IVsEditorAdaptersFactoryService? editorAdapters;
    private ITextUndoHistoryRegistry? undoRegistry;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        textManager = await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        var components = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        editorAdapters = components?.GetService<IVsEditorAdaptersFactoryService>();
        undoRegistry = components?.GetService<ITextUndoHistoryRegistry>();
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            commands.AddCommand(new MenuCommand(ExecuteProbe, new CommandID(CommandSet, 0x0100)));
            commands.AddCommand(new MenuCommand(ExecuteReplaceProbe, new CommandID(CommandSet, 0x0101)));
            commands.AddCommand(new MenuCommand(ExecuteBackgroundProbe, new CommandID(CommandSet, 0x0102)));
            commands.AddCommand(new MenuCommand(ExecuteFormatDocument, new CommandID(CommandSet, 0x0200)));
            commands.AddCommand(new MenuCommand(ExecuteFormatSelection, new CommandID(CommandSet, 0x0201)));
            commands.AddCommand(new MenuCommand(ExecuteFormatStatement, new CommandID(CommandSet, 0x0202)));
        }
    }

    private void ExecuteProbe(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string message = ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor)
            ? $"Active SQL buffer: {System.IO.Path.GetFileName(editor.Path)} ({editor.Text.Length} characters)."
            : "Open a .sql file in the text editor to inspect its buffer.";
        VsShellUtilities.ShowMessageBox(
            this,
            message,
            "T-SQL Formatter spike",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private void ExecuteReplaceProbe(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string message;
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor))
        {
            message = "Open a .sql file in the text editor first.";
        }
        else if (ErrorHandler.Failed(editor.View.GetSelectedText(out string selected)) || string.IsNullOrEmpty(selected))
        {
            message = "Select SQL text to run the replacement probe.";
        }
        else if (editorAdapters == null || undoRegistry == null)
        {
            message = "Visual Studio editor services are unavailable.";
        }
        else
        {
            message = editor.TryReplaceSelection(selected + " /* VSIX probe */", editorAdapters, undoRegistry)
                ? "Selection replaced with the original text plus a probe marker. Use Undo to revert."
                : "Selection could not be replaced.";
        }

        VsShellUtilities.ShowMessageBox(this, message, "T-SQL Formatter spike",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private void ExecuteBackgroundProbe(object sender, EventArgs e)
    {
        JoinableTaskFactory.RunAsync(BackgroundProbeAsync).FileAndForget("TSqlFormatter/BackgroundProbe");
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

    private async Task FormatStatementAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor) || editorAdapters == null || undoRegistry == null)
        {
            ShowProbeMessage("Open a .sql file in the text editor first.");
            return;
        }

        if (!editor.TryCaptureCaret(editorAdapters, out var snapshot, out int caret))
        {
            ShowProbeMessage("The SQL caret is unavailable.");
            return;
        }

        if (snapshot.Length > 16 * 1024 * 1024)
        {
            ShowProbeMessage("The SQL buffer exceeds 16 Mi characters.");
            return;
        }

        string source = snapshot.GetText();
        try
        {
            FormatResult result = await Task.Run(() => new ScriptDomSqlFormatter().Format(
                source, FormattingOptions.Default,
                new FormatRequest(FormatScope.Statement, new SqlTextSpan(caret, 0)), DisposalToken), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (result.Diagnostics.Count > 0)
            {
                var diagnostic = result.Diagnostics[0];
                ShowProbeMessage($"{diagnostic.Code}: {diagnostic.Message}");
            }
            else if (!result.Changed)
            {
                ShowProbeMessage("The SQL statement is already formatted.");
            }
            else if (result.Edits.Count != 1 ||
                     !ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     !editor.TryApplyEdit(snapshot, result.Edits[0].Span.StartOffset,
                         result.Edits[0].Span.Length, result.Edits[0].NewText,
                         "Format T-SQL Statement", editorAdapters, undoRegistry))
            {
                ShowProbeMessage("The SQL buffer changed during formatting; no edit was applied.");
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels formatting without applying an edit.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowProbeMessage($"Statement formatting failed: {ex.Message}");
        }
    }

    private async Task FormatSelectionAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor) || editorAdapters == null || undoRegistry == null)
        {
            ShowProbeMessage("Open a .sql file in the text editor first.");
            return;
        }

        if (!editor.TryCaptureSelection(editorAdapters, out var snapshot, out var selected))
        {
            ShowProbeMessage("Select SQL text in a .sql document first.");
            return;
        }

        if (snapshot.Length > 16 * 1024 * 1024)
        {
            ShowProbeMessage("The SQL buffer exceeds 16 Mi characters.");
            return;
        }

        string source = snapshot.GetText();
        try
        {
            var span = new SqlTextSpan(selected.Start, selected.Length);
            FormatResult result = await Task.Run(() => new ScriptDomSqlFormatter().Format(
                source, FormattingOptions.Default,
                new FormatRequest(FormatScope.Selection, span), DisposalToken), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (result.Diagnostics.Count > 0)
            {
                var diagnostic = result.Diagnostics[0];
                ShowProbeMessage($"{diagnostic.Code}: {diagnostic.Message}");
            }
            else if (!result.Changed)
            {
                ShowProbeMessage("The selected SQL statement is already formatted.");
            }
            else if (result.Edits.Count != 1 ||
                     !ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     !editor.TryApplyEdit(snapshot, result.Edits[0].Span.StartOffset,
                         result.Edits[0].Span.Length, result.Edits[0].NewText,
                         "Format T-SQL Selection", editorAdapters, undoRegistry))
            {
                ShowProbeMessage("The SQL buffer changed during formatting; no edit was applied.");
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels formatting without applying an edit.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowProbeMessage($"Selection formatting failed: {ex.Message}");
        }
    }

    private async Task FormatDocumentAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor) || editorAdapters == null || undoRegistry == null)
        {
            ShowProbeMessage("Open a .sql file in the text editor first.");
            return;
        }

        var snapshot = editor.CaptureSnapshot(editorAdapters);
        if (snapshot == null || snapshot.Length > 16 * 1024 * 1024)
        {
            ShowProbeMessage("The SQL buffer is unavailable or exceeds 16 Mi characters.");
            return;
        }

        string source = snapshot.GetText();
        try
        {
            FormatResult result = await Task.Run(() => new ScriptDomSqlFormatter().Format(
                source, FormattingOptions.Default, new FormatRequest(), DisposalToken), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (!result.ParseSucceeded || result.Diagnostics.Any(d => d.Severity == FormatterDiagnosticSeverity.Error))
            {
                var error = result.Diagnostics.FirstOrDefault();
                ShowProbeMessage(error == null ? "SQL could not be parsed." : $"{error.Code}: {error.Message}");
            }
            else if (!result.Changed)
            {
                ShowProbeMessage("The SQL document is already formatted.");
            }
            else if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current) ||
                     !ReferenceEquals(current.Buffer, editor.Buffer) ||
                     !editor.TryApplyDocument(snapshot, result.Text, editorAdapters, undoRegistry))
            {
                ShowProbeMessage("The SQL buffer changed during formatting; no edit was applied.");
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels formatting without applying an edit.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowProbeMessage($"Formatting failed: {ex.Message}");
        }
    }

    private async Task BackgroundProbeAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        if (!ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor editor))
        {
            ShowProbeMessage("Open a .sql file in the text editor first.");
            return;
        }

        string snapshot = editor.Text;
        try
        {
            int nonWhitespace = await Task.Run(() => CountNonWhitespace(snapshot, DisposalToken), DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (ActiveSqlEditor.TryRead(textManager, out ActiveSqlEditor current) &&
                ReferenceEquals(current.Buffer, editor.Buffer) && current.Text == snapshot)
            {
                ShowProbeMessage($"Background scan completed: {nonWhitespace} non-whitespace characters.");
            }
            else
            {
                ShowProbeMessage("The SQL buffer changed during the background scan; the result was discarded.");
            }
        }
        catch (OperationCanceledException)
        {
            // Package shutdown cancels the probe without touching the editor.
        }
        catch (Exception ex)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowProbeMessage($"Background scan failed: {ex.Message}");
        }
    }

    private static int CountNonWhitespace(string text, CancellationToken cancellationToken)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if ((i & 0x3fff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!char.IsWhiteSpace(text[i]))
            {
                count++;
            }
        }

        return count;
    }

    private void ShowProbeMessage(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(this, message, "T-SQL Formatter spike",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
