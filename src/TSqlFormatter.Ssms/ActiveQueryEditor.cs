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

    public bool TryCaptureSelection(IVsEditorAdaptersFactoryService adapters,
        out ITextSnapshot snapshot, out Span span)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        snapshot = null!;
        span = default;
        var textView = adapters.GetWpfTextView(View);
        if (textView == null || textView.Selection.IsEmpty) return false;
        var selected = textView.Selection.StreamSelectionSpan.SnapshotSpan;
        snapshot = textView.TextBuffer.CurrentSnapshot;
        if (!ReferenceEquals(selected.Snapshot, snapshot)) return false;
        span = selected.Span;
        return true;
    }

    public bool TryCaptureCaret(IVsEditorAdaptersFactoryService adapters,
        out ITextSnapshot snapshot, out int offset)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        snapshot = null!;
        offset = 0;
        var textView = adapters.GetWpfTextView(View);
        if (textView == null) return false;
        snapshot = textView.TextBuffer.CurrentSnapshot;
        var caret = textView.Caret.Position.BufferPosition;
        if (!ReferenceEquals(caret.Snapshot, snapshot)) return false;
        offset = caret.Position;
        return true;
    }

    public bool TryApplyDocument(ITextSnapshot original, string replacement,
        IVsEditorAdaptersFactoryService adapters, ITextUndoHistoryRegistry undoRegistry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryApplyEdit(original, 0, original.Length, replacement,
            "Format T-SQL Document", adapters, undoRegistry);
    }

    public bool TryApplyEdit(ITextSnapshot original, int start, int length, string replacement,
        string description, IVsEditorAdaptersFactoryService adapters, ITextUndoHistoryRegistry undoRegistry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var textView = adapters.GetWpfTextView(View);
        if (textView == null || !ReferenceEquals(textView.TextBuffer.CurrentSnapshot, original) ||
            start < 0 || length < 0 || start > original.Length - length)
            return false;

        int caret = textView.Caret.Position.BufferPosition.Position;
        var selection = textView.Selection.StreamSelectionSpan.SnapshotSpan;
        bool hasSelection = !textView.Selection.IsEmpty && ReferenceEquals(selection.Snapshot, original);
        bool reversed = textView.Selection.IsReversed;
        var history = undoRegistry.RegisterHistory(textView.TextBuffer);
        using (var transaction = history.CreateTransaction(description))
        {
            using (var edit = textView.TextBuffer.CreateEdit())
            {
                if (!edit.Replace(new Span(start, length), replacement)) return false;
                var updated = edit.Apply();
                if (hasSelection)
                {
                    int mappedStart = MapOffset(selection.Start.Position, start, length, replacement.Length);
                    int mappedEnd = MapOffset(selection.End.Position, start, length, replacement.Length);
                    textView.Selection.Select(new SnapshotSpan(updated, mappedStart, mappedEnd - mappedStart), reversed);
                }
                else
                    textView.Caret.MoveTo(new SnapshotPoint(updated,
                        MapOffset(caret, start, length, replacement.Length)));
            }

            transaction.Complete();
        }

        return true;
    }

    private static int MapOffset(int offset, int editStart, int oldLength, int newLength)
    {
        int editEnd = editStart + oldLength;
        if (offset <= editStart) return offset;
        if (offset >= editEnd) return offset + newLength - oldLength;
        return editStart + Math.Min(offset - editStart, newLength);
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
