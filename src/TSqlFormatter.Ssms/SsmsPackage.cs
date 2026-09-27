using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TSqlFormatter.Ssms;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuid)]
public sealed class SsmsPackage : AsyncPackage
{
    public const string PackageGuid = "908068E6-40D9-4543-AB9F-1B952930F1E3";
    private static readonly Guid CommandSet = new("4EE4F956-58EC-490D-9DCA-D2D198570CEC");

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        ActivityLog.LogInformation("T-SQL Formatter SSMS", "SSMS spike package initialized.");
        if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            commands.AddCommand(new MenuCommand(ExecuteLoadProbe, new CommandID(CommandSet, 0x0100)));
    }

    private void ExecuteLoadProbe(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ActivityLog.LogInformation("T-SQL Formatter SSMS", "Load Probe command executed.");
        VsShellUtilities.ShowMessageBox(this, "SSMS spike package loaded.", "T-SQL Formatter (SSMS)",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
