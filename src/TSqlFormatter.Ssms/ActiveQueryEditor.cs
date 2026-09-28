using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.Ssms;

internal sealed class ActiveQueryEditor
{
    private ActiveQueryEditor(string path, string text, IVsTextView view, IVsTextLines buffer)
    {
        Path = path;
        Text = text;
        View = view;
        Buffer = buffer;
    }

    public string Path { get; }
    public string Text { get; }
    public IVsTextView View { get; }
    public IVsTextLines Buffer { get; }

    public static bool TryRead(IVsTextManager? textManager, out ActiveQueryEditor editor)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        editor = null!;
        if (textManager == null ||
            ErrorHandler.Failed(textManager.GetActiveView(0, null, out IVsTextView view)) || view == null ||
            ErrorHandler.Failed(view.GetBuffer(out IVsTextLines buffer)) || buffer is not IVsUserData userData)
            return false;

        Guid monikerKey = VSConstants.VsTextBufferUserDataGuid.VsBufferMoniker_guid;
        if (ErrorHandler.Failed(userData.GetData(ref monikerKey, out object moniker)) ||
            moniker is not string path ||
            !string.Equals(System.IO.Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase) ||
            ErrorHandler.Failed(buffer.GetLastLineIndex(out int lastLine, out int lastIndex)) ||
            ErrorHandler.Failed(buffer.GetLineText(0, 0, lastLine, lastIndex, out string text)))
            return false;

        editor = new ActiveQueryEditor(path, text, view, buffer);
        return true;
    }
}
