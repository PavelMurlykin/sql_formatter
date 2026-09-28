using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
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

    public ITextSnapshot? CaptureSnapshot(IVsEditorAdaptersFactoryService adapters)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return adapters.GetWpfTextView(View)?.TextBuffer.CurrentSnapshot;
    }

    public bool TryApplyDocument(ITextSnapshot original, string replacement,
        IVsEditorAdaptersFactoryService adapters, ITextUndoHistoryRegistry undoRegistry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var textView = adapters.GetWpfTextView(View);
        if (textView == null || !ReferenceEquals(textView.TextBuffer.CurrentSnapshot, original))
            return false;

        int caret = textView.Caret.Position.BufferPosition.Position;
        var selection = textView.Selection.StreamSelectionSpan.SnapshotSpan;
        bool hasSelection = !textView.Selection.IsEmpty && ReferenceEquals(selection.Snapshot, original);
        bool reversed = textView.Selection.IsReversed;
        var history = undoRegistry.RegisterHistory(textView.TextBuffer);
        using (var transaction = history.CreateTransaction("Format T-SQL Document"))
        {
            using (var edit = textView.TextBuffer.CreateEdit())
            {
                if (!edit.Replace(new Span(0, original.Length), replacement)) return false;
                var updated = edit.Apply();
                if (hasSelection)
                {
                    int start = Math.Min(selection.Start.Position, updated.Length);
                    int end = Math.Min(selection.End.Position, updated.Length);
                    textView.Selection.Select(new SnapshotSpan(updated, start, end - start), reversed);
                }
                else
                    textView.Caret.MoveTo(new SnapshotPoint(updated, Math.Min(caret, updated.Length)));
            }

            transaction.Complete();
        }

        return true;
    }

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
