# 0004 — Start Visual Studio integration with a VSSDK spike

Status: Accepted as a spike direction

Date: 2026-09-22

## Context

The Visual Studio adapter must read the active SQL buffer, apply edits and preserve selection, caret and Undo behavior. Microsoft describes [VSSDK as its broadest extensibility model](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/extensibility-models), and [MEF is used for most editor features](https://learn.microsoft.com/en-us/visualstudio/extensibility/extending-the-editor-and-language-services). `VisualStudio.Extensibility` offers an out-of-process model, but its [preview status](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility) and scenario coverage must be evaluated for this editor workflow.

## Decision

Use VSSDK with MEF editor APIs as the first candidate for the Visual Studio compatibility spike in Phase 11. The spike must prove command registration, active `.sql` buffer access, selection replacement, Undo, caret restoration and installation in the targeted Visual Studio version. Select the extension target framework and concrete APIs from the official template and results of that spike. Do not place formatting rules in the extension.

This ADR chooses an investigation path, not a claim that the adapter already works. Revisit it if the spike exposes an unsupported API or a better-supported model. Microsoft's [Visual Studio 2026 compatibility guidance](https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/extension-compatibility) also requires testing behavior even when an older VSIX is expected to load.

## Consequences

- Core remains independent of Visual Studio; the adapter translates editor state and applies Core's edits.
- An in-process VSSDK adapter brings host runtime, threading and installation constraints that the spike must document.
- Visual Studio work starts only after the standalone formatter prototype is usable.

## Alternatives

- `VisualStudio.Extensibility` may be suitable later if its editor APIs satisfy the required workflow.
- Community Toolkit can simplify VSSDK code, but it wraps the same underlying model and does not change this decision.
