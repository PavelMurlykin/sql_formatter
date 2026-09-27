# 0006 — Use VSSDK commands and MEF text edits for the Visual Studio spike

Status: Accepted for the spike; runtime validation pending

Date: 2026-09-27

## Decision

Target the supported Visual Studio 17.x API surface from an in-process `net472` VSIX. Use VSSDK package/command services and native text-view APIs to locate and read the active `.sql` buffer; use the MEF editor adapter, `ITextBuffer` and text undo history for edits. Keep editor calls on the UI thread and move only immutable snapshots to background work. Keep `TSqlFormatter.Core` independent of Visual Studio assemblies.

## Evidence and constraints

The installed Visual Studio 2026 template pins `Microsoft.VisualStudio.SDK` 17.14.40265 and `Microsoft.VSSDK.BuildTools` 18.9.820; this project builds successfully with those versions. Microsoft's [compatibility guidance](https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility) says Visual Studio 2026 accepts supported 17.x APIs, but requires behavior testing. The [command implementation guide](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/command-implementation), [editor model](https://learn.microsoft.com/en-us/visualstudio/extensibility/inside-the-editor) and [undo history API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.text.operations.itextundohistoryregistry) support this split. We have not yet proved runtime installation, command visibility, actual Undo behavior, or caret positioning in the host; those remain explicit validation tasks.

## Consequences

- No SQL formatting logic belongs in the VSIX; the probes are temporary.
- Formatting may run off-thread only after copying editor text, then must revalidate the buffer before writing.
- The final command must present parse/configuration errors without partial edits.
- Host tests in Visual Studio 2026 and the targeted 2022 version are required before declaring the extension usable.
