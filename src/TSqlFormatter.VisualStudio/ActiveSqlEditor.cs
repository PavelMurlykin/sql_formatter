using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.VisualStudio;

internal sealed class ActiveSqlEditor
{
    private ActiveSqlEditor(string path, string text, IVsTextView view, IVsTextLines buffer)
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

    public bool TryReplaceSelection(string replacement)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var spans = new TextSpan[1];
        if (ErrorHandler.Failed(View.GetSelectionSpan(spans)))
        {
            return false;
        }

        TextSpan span = spans[0];
        if (span.iStartLine == span.iEndLine && span.iStartIndex == span.iEndIndex)
        {
            return false;
        }

        IntPtr nativeText = Marshal.StringToCoTaskMemUni(replacement);
        try
        {
            return ErrorHandler.Succeeded(Buffer.ReplaceLines(
                span.iStartLine, span.iStartIndex, span.iEndLine, span.iEndIndex,
                nativeText, replacement.Length, new TextSpan[1]));
        }
        finally
        {
            Marshal.FreeCoTaskMem(nativeText);
        }
    }

    public static bool TryRead(IVsTextManager? textManager, out ActiveSqlEditor editor)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        editor = null!;
        if (textManager == null || ErrorHandler.Failed(textManager.GetActiveView(0, null, out IVsTextView view)) || view == null ||
            ErrorHandler.Failed(view.GetBuffer(out IVsTextLines buffer)) || buffer is not IVsUserData userData)
        {
            return false;
        }

        Guid monikerKey = VSConstants.VsTextBufferUserDataGuid.VsBufferMoniker_guid;
        if (ErrorHandler.Failed(userData.GetData(ref monikerKey, out object moniker)) ||
            moniker is not string path || !string.Equals(System.IO.Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase) ||
            ErrorHandler.Failed(buffer.GetLastLineIndex(out int lastLine, out int lastIndex)) ||
            ErrorHandler.Failed(buffer.GetLineText(0, 0, lastLine, lastIndex, out string text)))
        {
            return false;
        }

        editor = new ActiveSqlEditor(path, text, view, buffer);
        return true;
    }
}
