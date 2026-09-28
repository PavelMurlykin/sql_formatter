using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.Ssms;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuid)]
public sealed class SsmsPackage : AsyncPackage
{
    public const string PackageGuid = "908068E6-40D9-4543-AB9F-1B952930F1E3";
    private static readonly Guid CommandSet = new("4EE4F956-58EC-490D-9DCA-D2D198570CEC");
    private IVsTextManager? textManager;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        textManager = await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        ActivityLog.LogInformation("T-SQL Formatter SSMS", "SSMS spike package initialized.");
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
        {
            commands.AddCommand(new MenuCommand(ExecuteLoadProbe, new CommandID(CommandSet, 0x0100)));
            commands.AddCommand(new MenuCommand(ExecuteEditorProbe, new CommandID(CommandSet, 0x0101)));
        }
    }

    private void ExecuteLoadProbe(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ActivityLog.LogInformation("T-SQL Formatter SSMS", "Load Probe command executed.");
        VsShellUtilities.ShowMessageBox(this, "SSMS spike package loaded.", "T-SQL Formatter (SSMS)",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private void ExecuteEditorProbe(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string message = ActiveQueryEditor.TryRead(textManager, out ActiveQueryEditor editor)
            ? $"Active SQL query: {System.IO.Path.GetFileName(editor.Path)} ({editor.Text.Length} characters)."
            : "Open a .sql file in the SSMS query editor first.";
        ActivityLog.LogInformation("T-SQL Formatter SSMS", message);
        VsShellUtilities.ShowMessageBox(this, message, "T-SQL Formatter (SSMS)",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
