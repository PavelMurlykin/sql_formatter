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
    private ActiveSqlEditor(string path, IVsTextView view, IVsTextLines buffer)
    {
        Path = path;
        View = view;
        Buffer = buffer;
    }

    public string Path { get; }
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
        if (textView == null || textView.Selection.IsEmpty)
        {
            return false;
        }

        var selected = textView.Selection.StreamSelectionSpan.SnapshotSpan;
        snapshot = textView.TextBuffer.CurrentSnapshot;
        if (!ReferenceEquals(selected.Snapshot, snapshot))
        {
            return false;
        }

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
        if (textView == null)
        {
            return false;
        }

        snapshot = textView.TextBuffer.CurrentSnapshot;
        var caret = textView.Caret.Position.BufferPosition;
        if (!ReferenceEquals(caret.Snapshot, snapshot))
        {
            return false;
        }

        offset = caret.Position;
        return true;
    }

    public bool TryApplyDocument(ITextSnapshot original, string replacement,
        IVsEditorAdaptersFactoryService adapters, ITextUndoHistoryRegistry undoRegistry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TryApplyEdit(original, 0, original.Length, replacement, "Format T-SQL Document", adapters, undoRegistry);
    }

    public bool TryApplyEdit(ITextSnapshot original, int start, int length, string replacement,
        string description, IVsEditorAdaptersFactoryService adapters, ITextUndoHistoryRegistry undoRegistry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var textView = adapters.GetWpfTextView(View);
        if (textView == null || !ReferenceEquals(textView.TextBuffer.CurrentSnapshot, original) ||
            start < 0 || length < 0 || start > original.Length - length)
        {
            return false;
        }

        var selection = textView.Selection.StreamSelectionSpan.SnapshotSpan;
        bool hasSelection = !textView.Selection.IsEmpty && ReferenceEquals(selection.Snapshot, original);
        bool reversed = textView.Selection.IsReversed;
        int caret = textView.Caret.Position.BufferPosition.Position;
        ITextUndoHistory history = undoRegistry.RegisterHistory(textView.TextBuffer);
        using (ITextUndoTransaction transaction = history.CreateTransaction(description))
        {
            using (ITextEdit edit = textView.TextBuffer.CreateEdit())
            {
                if (!edit.Replace(new Span(start, length), replacement))
                {
                    return false;
                }

                ITextSnapshot updated = edit.Apply();
                if (hasSelection)
                {
                    int mappedStart = MapOffset(selection.Start.Position, start, length, replacement.Length);
                    int mappedEnd = MapOffset(selection.End.Position, start, length, replacement.Length);
                    textView.Selection.Select(new SnapshotSpan(updated, mappedStart, mappedEnd - mappedStart), reversed);
                }
                else
                {
                    textView.Caret.MoveTo(new SnapshotPoint(updated,
                        MapOffset(caret, start, length, replacement.Length)));
                }
            }

            transaction.Complete();
        }

        return true;
    }

    private static int MapOffset(int offset, int editStart, int oldLength, int newLength)
    {
        if (oldLength == 0 && offset == editStart) return editStart + newLength;
        int editEnd = editStart + oldLength;
        if (offset <= editStart) return offset;
        if (offset >= editEnd) return offset + newLength - oldLength;
        return editStart + Math.Min(offset - editStart, newLength);
    }

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

    public static bool TryRead(IVsTextManager? textManager, out ActiveSqlEditor editor,
        Microsoft.VisualStudio.Shell.Interop.IVsMonitorSelection? selection = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread(); editor = null!;
        if (!TSqlFormatter.IdeShared.ActiveDocumentView.TryGet(selection, textManager, out var view, out var frameMoniker) ||
            ErrorHandler.Failed(view.GetBuffer(out IVsTextLines buffer))) return false;
        string? path = TSqlFormatter.IdeShared.ActiveDocumentView.Moniker(buffer, frameMoniker);
        if (path is null || !string.Equals(System.IO.Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase)) return false;
        editor = new ActiveSqlEditor(path, view, buffer); return true;
    }
}
