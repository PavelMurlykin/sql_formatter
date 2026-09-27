using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuid)]
public sealed class SqlFormatterPackage : AsyncPackage
{
    public const string PackageGuid = "C8BAF105-2C0F-484C-93DB-DC653596796B";
    private static readonly Guid CommandSet = new("164528C8-0EC5-4EEC-8380-7C17745D9701");
    private IVsTextManager? textManager;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        textManager = await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            commands.AddCommand(new MenuCommand(ExecuteProbe, new CommandID(CommandSet, 0x0100)));
            commands.AddCommand(new MenuCommand(ExecuteReplaceProbe, new CommandID(CommandSet, 0x0101)));
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
        else
        {
            message = editor.TryReplaceSelection(selected + " /* VSIX probe */")
                ? "Selection replaced with the original text plus a probe marker. Use Undo to revert."
                : "Selection could not be replaced.";
        }

        VsShellUtilities.ShowMessageBox(this, message, "T-SQL Formatter spike",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
