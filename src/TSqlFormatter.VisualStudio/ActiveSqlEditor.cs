using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
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

    public bool TryReplaceSelection(string replacement, IVsEditorAdaptersFactoryService adapters, ITextUndoHistoryRegistry undoRegistry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var textView = adapters.GetWpfTextView(View);
        if (textView == null || textView.Selection.IsEmpty)
        {
            return false;
        }

        SnapshotSpan selected = textView.Selection.StreamSelectionSpan.SnapshotSpan;
        ITextBuffer textBuffer = textView.TextBuffer;
        if (!ReferenceEquals(selected.Snapshot.TextBuffer, textBuffer))
        {
            return false;
        }

        bool reversed = textView.Selection.IsReversed;
        int start = selected.Start.Position;
        int originalLength = selected.Length;
        ITextUndoHistory history = undoRegistry.RegisterHistory(textBuffer);
        using (ITextUndoTransaction transaction = history.CreateTransaction("T-SQL Formatter selection probe"))
        {
            using (ITextEdit edit = textBuffer.CreateEdit())
            {
                if (!edit.Replace(selected.Span, replacement))
                {
                    return false;
                }

                ITextSnapshot updated = edit.Apply();
                textView.Selection.Select(new SnapshotSpan(updated, start, originalLength), reversed);
            }

            transaction.Complete();
        }

        return true;
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
