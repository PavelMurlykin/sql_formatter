using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.IdeShared;

/// <summary>Moves the caret of a text view to a one-based line and column.</summary>
public static class SqlEditorNavigation
{
    public static void GoTo(IVsTextView view, int line, int column)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (view is null)
        {
            return;
        }

        var targetLine = Math.Max(0, line - 1);
        var targetColumn = Math.Max(0, column - 1);
        view.SetCaretPos(targetLine, targetColumn);
        view.SetSelection(targetLine, targetColumn, targetLine, targetColumn);
        view.CenterLines(targetLine, 1);
    }
}
