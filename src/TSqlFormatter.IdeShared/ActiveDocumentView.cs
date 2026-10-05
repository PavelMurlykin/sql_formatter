using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.IdeShared;

internal static class ActiveDocumentView
{
    internal static bool TryGet(IVsMonitorSelection? selection, IVsTextManager? manager,
        out IVsTextView view, out string? moniker)
    {
        ThreadHelper.ThrowIfNotOnUIThread(); view = null!; moniker = null;
        // The document frame survives menu/tool-window focus changes. Never cache a previous SQL file.
        if (selection is not null && ErrorHandler.Succeeded(selection.GetCurrentElementValue(
            (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object value)) && value is IVsWindowFrame frame)
        {
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out object path); moniker = path as string;
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object documentView);
            if (documentView is IVsCodeWindow codeWindow)
            {
                if (ErrorHandler.Failed(codeWindow.GetLastActiveView(out view)) || view is null) codeWindow.GetPrimaryView(out view);
            }
            else view = documentView as IVsTextView ?? null!;
            if (view is not null) return true;
        }
        return manager is not null && ErrorHandler.Succeeded(manager.GetActiveView(0, null, out view)) && view is not null;
    }
    internal static string? Moniker(IVsTextLines buffer, string? frameMoniker)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!string.IsNullOrEmpty(frameMoniker)) return frameMoniker;
        if (buffer is IVsUserData data)
        {
            Guid key = VSConstants.VsTextBufferUserDataGuid.VsBufferMoniker_guid;
            if (ErrorHandler.Succeeded(data.GetData(ref key, out object value)) && value is string path && path.Length > 0) return path;
        }
        if (buffer is IPersistFileFormat persisted && ErrorHandler.Succeeded(persisted.GetCurFile(out string file, out _))) return file;
        return null;
    }
}
