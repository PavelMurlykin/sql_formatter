# SQL Complete capability audit (SC-27)

Validation date: 2026-10-01. This is configurable-capability coverage of the
two supplied profiles, not a SQL Complete output-equivalence certification.
SC-27 is complete within the version-specific preview boundaries below.

## Inventory and executable evidence

The immutable `tests/TSqlFormatter.Core.Tests/SqlCompleteParity/profile-values.tsv`
has SHA-256 `51FC02E8E1AD6CB0487AA4B419BC543560DD2A4D93A7F73B03ED904581B66829`.
The ledger accounts for 577 top-level groups and 977 scalar paths:
969 `covered`, 8 `not_applicable`, zero unresolved paths. The only exclusions
are the user-approved Subquery OptionHints paths (2026-09-30).

`FinalParityAuditTests` verifies every path, status, native typed editable
mapping and referenced executable Fact/Theory method, and regenerates the
correspondence report with snapshot/hash/path-set validation. A referenced
test is evidence, not a claim that every SQL syntax combination has been
exhaustively tested. Existing rule suites check alternative values, scopes,
dependencies, safe refusal, literals/comments, v1/v2 and CLI behavior.

The two original native alternatives are ReadableVertical and CompactQueries,
not conversions of AV_Profile or Right aligned EPM-AWB2. Without verified
control output for XML numeric enums/Style, the SC-26 fallback remains the
explicit decision. Private source XML is not published.

## Golden and adapter checks

`ParityGoldenTests` checks 82 golden cases: two alternatives times 41 inputs
(20 inventory categories plus 21 edge cases). The edge corpus includes CTEs,
window functions, nested queries, CASE/set chains, DML/OUTPUT, MERGE, cursors,
control flow, modules, temporal DDL, DDL/LOGON triggers, dynamic EXEC,
comments, multiline literals and legacy COMPUTE. Each row checks reviewed
expected text, parsing, diagnostics, idempotence and exact tokens with
keyword casing preserved. The two smoke presets also match Core, draft
preview and CLI under identical effective configuration in automated tests.

Local results: 1143 Core tests and 145 golden tests pass; Debug/Release
solution builds have zero errors/warnings. Both generator check modes pass
without changing artifacts. Visual Studio VSIX 0.2.4 passes package validation.
The local candidate packages are VS 0.2.4, SSMS 0.6.3 and CLI 0.1.0-preview.6;
the candidate procedure passed all 1288 tests, both VSIX checks, package
creation and an installed-CLI smoke in `artifacts/parity-final-candidate/`.
Both final VSIXs installed with exit 0 and their installed manifests were
verified. In VS Community 2026 18.10.2 and SSMS 22.10.2 x64, All settings,
ReadableVertical import, OK persistence, reopening, visibly multiline preview,
Document on EXEC/SELECT/CREATE TABLE, save and one Undo restoring the original
file passed. SSMS additionally exercised typed search/edit/Set value and
Cancel rollback, plus project preview showing the repository's .editorconfig.
SSMS's host ScriptDom is 18.0.105.0; its CREATE TABLE case completed without
a missing-member error. These narrow checks do not certify other assemblies.
Both tests used disposable files without database connections or SQL execution;
full settings was disabled afterward to restore the original command mode.
The first VS install attempt returned 2004 while VS was closing; waiting for
process exit resolved it. The user dismissed Connect/security prompts; no
security dialog was automated and no IDE update was authorized.
Installed-IDE findings and remaining untested workflows belong in
[Visual Studio smoke](visual-studio-smoke.md) and
[SSMS compatibility](ssms22-compatibility.md); build checks do not prove UI behavior.

Reproduce from the repository root with restored pinned dependencies:

```powershell
dotnet build TSqlFormatter.sln --no-restore -c Debug
dotnet build TSqlFormatter.sln --no-restore -c Release
dotnet test tests/TSqlFormatter.Core.Tests --no-restore
dotnet test tests/TSqlFormatter.GoldenTests --no-restore
pwsh -NoProfile -File scripts/Export-NativeProfileAlternatives.ps1
pwsh -NoProfile -File scripts/Export-ParityGoldenCorpus.ps1
dotnet run --no-build -c Release --project benchmarks/TSqlFormatter.Benchmarks -- --parity-validation
```

Golden `-Write` deliberately regenerates the baseline; review output changes
before using it. A regenerated baseline alone is not a correctness check.

## Time and allocated memory

Warm synchronous end-to-end Release/.NET 10 measurements on the local Windows
host, 2026-10-01. Inputs are synthetic procedures with 100/300/1000 SET
assignments. A small procedure warms each profile before measurement; GC is
collected between sizes. Allocated bytes use
`GC.GetAllocatedBytesForCurrentThread`, not peak or retained process memory.
The second format checks stability outside the timed/allocated interval.

| Profile | Characters | Elapsed ms | Allocated MiB | Stable |
| --- | ---: | ---: | ---: | --- |
| Default v1 | 1813 | 2.4 | 0.76 | yes |
| Default v1 | 5213 | 7.9 | 2.07 | yes |
| Default v1 | 17113 | 22.1 | 6.43 | yes |
| ReadableVertical | 1813 | 25.8 | 9.10 | yes |
| ReadableVertical | 5213 | 75.2 | 26.28 | yes |
| ReadableVertical | 17113 | 497.3 | 77.63 | yes |
| CompactQueries | 1813 | 17.1 | 9.73 | yes |
| CompactQueries | 5213 | 39.5 | 28.14 | yes |
| CompactQueries | 17113 | 175.6 | 83.39 | yes |

The executable guard allows at most 2 seconds and 128 MiB allocated for inputs
up to 8192 characters. All measured cases within that size pass. This is a
generous local regression guard, not a latency SLA or installed-IDE UI
responsiveness claim. Parsing cancellation remains cooperative; large
inputs can exceed a nominal cancellation deadline.

## Compatibility and deliberate boundaries

| Area | Contract / limitation |
| --- | --- |
| Native JSON | v1 retains earlier behavior; v2 rules are opt-in typed overrides. Explicit fields overlay by the documented precedence. |
| SQL Complete XML | No numeric-mode guessing or reliable AV/EPM converter. The correspondence report preserves original values and describes native alternatives. |
| Native presets | Selected overrides, not universal vertical/compact rewriting. Unspecified boundaries/indents retain defaults; complex UPDATE/OUTPUT, MERGE tails, cursor queries and module bodies can therefore retain their source layout. All applicable controls remain editable separately. Golden cases record these intentional preset results, not SQL Complete reference output. |
| OptionHints in subqueries | Eight paths excluded only by the recorded user decision; no fabricated settings. |
| Comments / literals | Token-gap policies skip unsafe comment-adjacent edits; invalid SQL and scripts with multiline literals/quoted identifiers are not structurally rewritten. Dynamic SQL strings are never formatted internally. |
| Legacy TRY/CATCH | Default/v1 retains its historical omission of a trailing TRY/CATCH semicolon. An explicit `code.*` override preserves it. The legacy boundary is documented, not presented as token-perfect output for every default/v1 script. |
| CREATE TABLE | Defined ordinary/temporal/graph forms are configurable; CTAS/CLONE and definition-free forms are safely skipped. |
| CLR modules / triggers | Headers are configurable; EXTERNAL NAME is not a SQL body. |
| Dialect | Auto reparses with supported ScriptDom dialects, including legacy COMPUTE fallback; parse-unsupported SQL is unchanged. |
| IDEs | Version-specific experimental adapters. Installation target ranges do not certify every host version or ScriptDom API surface. |
| Distribution | Local preview packages only. No stable release, publication, signing or owner distribution decision is implied. |
