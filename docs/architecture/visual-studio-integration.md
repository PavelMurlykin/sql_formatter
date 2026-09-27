# Visual Studio integration (Phase 11 spike)

## Boundary

`TSqlFormatter.VisualStudio` is a `net472` in-process VSSDK VSIX. It owns commands and editor state only; formatting rules stay in `TSqlFormatter.Core` (`netstandard2.0`). The VSIX now has a prototype Format Document command and diagnostic probes. Its assets include the package assembly, Core and ScriptDom assemblies, compiled command table and `.pkgdef`.

## Confirmed compile-time API path

1. `AsyncPackage`, `ProvideMenuResource`, a `.vsct` command table, and `OleMenuCommandService` register Tools-menu commands.
2. `SVsTextManager` / `IVsTextManager.GetActiveView` locate a text view. `IVsTextView.GetBuffer` and `IVsTextLines.GetLineText` read the live buffer. `IVsUserData` / `VsBufferMoniker_guid` require a `.sql` file.
3. `SComponentModel` supplies `IVsEditorAdaptersFactoryService`, which maps the native view to `IWpfTextView`. The probe uses `ITextBuffer.CreateEdit` within `ITextUndoHistoryRegistry` / `ITextUndoTransaction`, then restores selection direction and caret side.
4. The background probe captures an immutable string on the UI thread, scans it in `Task.Run`, then switches back to the UI thread. It discards the result if the active buffer has changed. No editor COM or MEF API is called on the worker thread.

These APIs compile against `Microsoft.VisualStudio.SDK` 17.14.40265 and `Microsoft.VSSDK.BuildTools` 18.9.820 (versions from the installed Visual Studio 2026 project template). The VSIX builds on Visual Studio Community 2026 18.10.1. Runtime installation, command invocation, Undo/caret behavior, and compatibility with Visual Studio 2022 remain to be checked in an experimental instance; successful compilation is not a runtime compatibility claim.

## Next production path

The MVP Document and Selection commands use Core formatting of a captured SQL snapshot and reject stale results before applying an edit. Selection currently chooses a top-level statement; nested minimal-ancestor selection, Statement by caret, project configuration, and richer diagnostics remain. Limit commands to recognized SQL documents. Never parse SQL on the UI thread; keep the final buffer edit and caret handling on that thread. Do not treat the spike's background character scan as a formatter benchmark.

## Manual validation checklist

- Install the built VSIX in the Visual Studio 2026 experimental instance (`/RootSuffix Exp`).
- Open a disposable `.sql` file; verify Tools-menu registration and that the read probe counts unsaved edits.
- Select text in both directions; run Replace Selection Probe; verify the marker, selection/caret, one-step Undo and Redo.
- Switch documents during Background Probe; verify that stale results are discarded.
- Open a non-SQL file; verify probes do not edit it.

Microsoft API references: [extension compatibility](https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility), [command implementation](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/command-implementation), [active text view](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.textmanager.interop.ivstextmanager.getactiveview), [editor text model](https://learn.microsoft.com/en-us/visualstudio/extensibility/inside-the-editor), [undo history](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.text.operations.itextundohistoryregistry).
