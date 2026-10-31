using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using TSqlFormatter.Configuration;
using TSqlFormatter.Core.History;

namespace TSqlFormatter.IdeShared;

/// <summary>RDT and DTE lifetime hooks. Captures SQL only, before discarded buffers disappear.</summary>
internal sealed class EditorHistoryIntegration : IVsRunningDocTableEvents3, IDisposable
{
    private readonly IVsRunningDocumentTable table;
    private readonly IVsEditorAdaptersFactoryService? adapters;
    private readonly EnvDTE.DTE? dte;
    private readonly Func<EditorFeaturesSettings> settings;
    private readonly Func<uint, int>? beforeSave;
    private readonly EditorHistorySession session = new();
    private readonly Dictionary<uint, Tracked> documents = new();
    private readonly Dictionary<uint, EditorSnapshot> closing = new();
    private readonly EnvDTE.DocumentEvents? documentEvents;
    private readonly JoinableTaskCollection work;
    private readonly JoinableTaskFactory factory;
    private uint subscription;

    internal EditorHistoryIntegration(IVsRunningDocumentTable table, IVsEditorAdaptersFactoryService? adapters,
        EnvDTE.DTE? dte, Func<EditorFeaturesSettings> settings, Func<uint, int>? beforeSave = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.table = table; this.adapters = adapters; this.dte = dte; this.settings = settings; this.beforeSave = beforeSave;
        work = ThreadHelper.JoinableTaskContext.CreateCollection();
        factory = ThreadHelper.JoinableTaskContext.CreateFactory(work);
        ErrorHandler.ThrowOnFailure(table.AdviseRunningDocTableEvents(this, out subscription));
        try
        {
            documentEvents = dte?.Events.DocumentEvents;
            if (documentEvents != null) documentEvents.DocumentClosing += DocumentClosing;
        }
        catch (Exception ex) { Log("DTE closing hook unavailable: " + ex.GetType().Name); }
        Refresh();
    }

    internal void Refresh()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var current = settings();
        if (!current.HistoryEnabled) { session.Clear(); closing.Clear(); return; }
        if (ErrorHandler.Succeeded(table.GetRunningDocumentsEnum(out var iterator)))
        {
            var cookies = new uint[1];
            while (iterator.Next(1, cookies, out uint fetched) == VSConstants.S_OK && fetched == 1) Remember(cookies[0]);
        }
        var store = current.CreateHistoryStore();
        Queue(() => store.Prune());
    }

    private void Remember(uint cookie)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        IntPtr pointer = IntPtr.Zero;
        try
        {
            var current = settings();
            if (!current.HistoryEnabled) { session.Forget(cookie); closing.Remove(cookie); return; }
            if (ErrorHandler.Failed(table.GetDocumentInfo(cookie, out _, out _, out _, out string moniker,
                out _, out _, out pointer)) || pointer == IntPtr.Zero) return;
            var data = Marshal.GetObjectForIUnknown(pointer);
            var lines = data as IVsTextLines;
            ITextBuffer? buffer = data is IVsTextBuffer adapted ? adapters?.GetDocumentBuffer(adapted) : null;
            bool sql = string.Equals(Path.GetExtension(moniker), ".sql", StringComparison.OrdinalIgnoreCase) ||
                buffer?.ContentType.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!sql) { session.Forget(cookie); return; }
            if (!documents.TryGetValue(cookie, out var tracked))
            {
                tracked = new Tracked(moniker, buffer);
                documents.Add(cookie, tracked);
                if (buffer != null)
                {
                    tracked.Changed = (_, _) => BufferChanged(cookie);
                    buffer.Changed += tracked.Changed;
                }
            }
            tracked.Path = moniker;
            string? text = null;
            if (buffer != null && buffer.CurrentSnapshot.Length <= current.HistoryMaxCharacters)
                text = buffer.CurrentSnapshot.GetText();
            else if (lines != null && ErrorHandler.Succeeded(lines.GetLastLineIndex(out int lastLine, out int lastColumn)) &&
                ErrorHandler.Succeeded(lines.GetLineText(0, 0, lastLine, lastColumn, out string value))) text = value;
            if (text == null) return;
            var context = ConnectionContext(moniker);
            session.Remember(cookie, moniker, text, true, current.HistoryEnabled, current.HistoryMaxCharacters,
                context.Server, context.Database);
        }
        catch (Exception ex) { Log("Capture unavailable: " + ex.GetType().Name); }
        finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
    }

    private void BufferChanged(uint cookie)
    {
        if (ThreadHelper.CheckAccess()) Remember(cookie);
        else factory.Run(async () => { await factory.SwitchToMainThreadAsync(); Remember(cookie); });
    }

    private void DocumentClosing(EnvDTE.Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            string path = document.FullName;
            foreach (var pair in documents.ToArray())
            {
                if (!string.Equals(pair.Value.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                Remember(pair.Key);
                var snapshot = session.Take(pair.Key, "close");
                if (snapshot == null) continue;
                // Keep this copy if the shell subsequently reloads the saved file
                // while discarding edits, before releasing its final RDT lock.
                closing[pair.Key] = snapshot;
                Save(snapshot);
            }
        }
        catch (Exception ex) { Log("DTE close capture unavailable: " + ex.GetType().Name); }
    }

    private void Save(EditorSnapshot? snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (snapshot == null) return;
        var store = settings().CreateHistoryStore();
        if (!store.Enabled) return;
        Queue(() =>
        {
            var path = store.Save(snapshot.FilePath, snapshot.Content, snapshot.TimestampUtc,
                snapshot.Server, snapshot.Database, snapshot.Reason);
            Log(path == null ? "Snapshot skipped by storage policy." : "Snapshot stored (" + snapshot.Reason + ").");
        });
    }

    private void Queue(Action action) => factory.RunAsync(async () =>
    {
        await TaskScheduler.Default;
        try { action(); }
        catch (Exception ex) { Log("History storage unavailable: " + ex.GetType().Name); }
    }).FileAndForget("TSqlFormatter/HistoryStorage");

    private void Drain()
    {
        // The waiter must not join the collection it is waiting to empty.
        // Using the history factory here would add the waiter to work itself.
        using (work.Join()) ThreadHelper.JoinableTaskFactory.Run(async () => await work.JoinTillEmptyAsync());
    }

    private (string? Server, string? Database) ConnectionContext(string moniker)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (dte?.ActiveDocument is not { } active ||
                !string.Equals(active.FullName, moniker, StringComparison.OrdinalIgnoreCase)) return (null, null);
            // Only inspect the active SSMS document's existing context. No
            // connection is opened and no connection string/credential is stored.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache", false))
                .FirstOrDefault(t => t != null);
            var factory = type?.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var info = factory?.GetType().GetProperty("CurrentlyActiveWndConnectionInfo")?.GetValue(factory);
            if (info == null) return (null, null);
            var server = info.GetType().GetProperty("ServerName")?.GetValue(info) as string;
            var database = info.GetType().GetProperty("DatabaseName")?.GetValue(info) as string;
            if (database == null && info.GetType().GetProperty("AdvancedOptions")?.GetValue(info) is IDictionary options)
                database = options["DATABASE"] as string;
            return (server, database);
        }
        catch (Exception) { return (null, null); }
    }

    public int OnBeforeSave(uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); Remember(cookie); return beforeSave?.Invoke(cookie) ?? VSConstants.S_OK; }
    public int OnAfterSave(uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); Remember(cookie); Save(session.Take(cookie, "save")); return VSConstants.S_OK; }
    public int OnAfterFirstDocumentLock(uint cookie, uint kind, uint read, uint edit) { ThreadHelper.ThrowIfNotOnUIThread(); Remember(cookie); return VSConstants.S_OK; }
    public int OnBeforeLastDocumentUnlock(uint cookie, uint kind, uint read, uint edit)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (read != 0 || edit != 0) return VSConstants.S_OK;
        if (!closing.ContainsKey(cookie)) { Remember(cookie); Save(session.Take(cookie, "close", true)); }
        closing.Remove(cookie); session.Forget(cookie);
        if (documents.TryGetValue(cookie, out var tracked))
        {
            if (tracked.Buffer != null && tracked.Changed != null) tracked.Buffer.Changed -= tracked.Changed;
            documents.Remove(cookie);
        }
        // Complete local IO before the host can exit after closing its last tab.
        Drain();
        return VSConstants.S_OK;
    }
    public int OnBeforeDocumentWindowShow(uint cookie, int first, IVsWindowFrame frame) { ThreadHelper.ThrowIfNotOnUIThread(); Remember(cookie); return VSConstants.S_OK; }
    public int OnAfterDocumentWindowHide(uint cookie, IVsWindowFrame frame) => VSConstants.S_OK;
    public int OnAfterAttributeChange(uint cookie, uint attributes) { ThreadHelper.ThrowIfNotOnUIThread(); Remember(cookie); return VSConstants.S_OK; }
    public int OnAfterAttributeChangeEx(uint cookie, uint attributes, IVsHierarchy oldHierarchy, uint oldItem,
        string oldMoniker, IVsHierarchy newHierarchy, uint newItem, string newMoniker) { ThreadHelper.ThrowIfNotOnUIThread(); Remember(cookie); return VSConstants.S_OK; }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (documentEvents != null) documentEvents.DocumentClosing -= DocumentClosing;
        if (subscription != 0) { table.UnadviseRunningDocTableEvents(subscription); subscription = 0; }
        foreach (var tracked in documents.Values)
            if (tracked.Buffer != null && tracked.Changed != null) tracked.Buffer.Changed -= tracked.Changed;
        documents.Clear(); closing.Clear(); session.Clear(); Drain();
    }

    private static void Log(string message) => ActivityLog.LogInformation("T-SQL Formatter history", message);
    private sealed class Tracked
    {
        internal Tracked(string path, ITextBuffer? buffer) { Path = path; Buffer = buffer; }
        internal string Path;
        internal readonly ITextBuffer? Buffer;
        internal EventHandler<TextContentChangedEventArgs>? Changed;
    }
}
