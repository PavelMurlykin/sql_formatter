using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace TSqlFormatter.VisualStudio;

/// <summary>RDT adapter; only the before-save event changes editor behavior.</summary>
internal sealed class SaveEventSink : IVsRunningDocTableEvents3
{
    private readonly Func<uint, int> beforeSave;

    public SaveEventSink(Func<uint, int> beforeSave) => this.beforeSave = beforeSave;

    public int OnBeforeSave(uint docCookie) => beforeSave(docCookie);

    public int OnAfterSave(uint docCookie) => VSConstants.S_OK;

    public int OnAfterFirstDocumentLock(uint docCookie, uint lockType, uint readLocks, uint editLocks)
        => VSConstants.S_OK;

    public int OnBeforeLastDocumentUnlock(uint docCookie, uint lockType, uint readLocks, uint editLocks)
        => VSConstants.S_OK;

    public int OnAfterAttributeChange(uint docCookie, uint attributes) => VSConstants.S_OK;

    public int OnBeforeDocumentWindowShow(uint docCookie, int firstShow, IVsWindowFrame frame)
        => VSConstants.S_OK;

    public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame frame) => VSConstants.S_OK;

    public int OnAfterAttributeChangeEx(uint docCookie, uint attributes,
        IVsHierarchy oldHierarchy, uint oldItemId, string oldMoniker,
        IVsHierarchy newHierarchy, uint newItemId, string newMoniker)
        => VSConstants.S_OK;
}
