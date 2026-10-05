# Native import profiles

Delivery copies of ADIR_SQL_Main, AV_Profile and Right-aligned-EPM-AWB2 are in
[`release/`](../../release/) beside the four current installers (VS 0.2.9 / SSMS
0.6.8). The release builder copies these canonical sources and verifies all three
with the libraries extracted from every package; see
[installation](../../release/README.en.md) and [validation](../../release/VALIDATION.md).

`ADIR_SQL_Main.json` is a native v2 import profile mapped from the user-supplied SQL Prompt
ADIR_SQL_Main exports and all 16 settings screenshots, refined against 2026 Git files
in Functions/Tables/Triggers/Types/Views. The refined profile requires Visual Studio
0.2.7 or SSMS 0.6.6; import it again to update a saved profile. It uses width 160, four spaces,
leading commas and opt-in shared layout policies. Install the newly built VSIX before
importing this profile: older versions do not recognize the added rules. Import JSON on
the Profiles page, save as ADIR_SQL_Main, and optionally select it as the default. Project
configuration takes precedence. The [mapping and limitations](../../docs/sql-prompt-adir-profile.md)
and [current local corpus audit](../../docs/sql-prompt-adir-additional-validation.md) distinguish native settings
from verified output matches. This does not add a generic SQL Prompt style-file importer.


`AV_Profile.json` and `Right-aligned-EPM-AWB2.json` are working JSON v2 profiles
prepared from the supplied SQL Complete exports. Each contains 573 native rules; install
Visual Studio 0.2.8 or SSMS 0.6.7 before importing. Import each file on Profiles, save it
under its name, and optionally select it as the default. Project configuration takes
precedence. Both choose width 120, four spaces and CRLF because the exports do not include
these general editor settings. New relativeSpaces/absoluteSpaces offsets are literal
spaces; legacy offsets retain their indent-level units. Both source exports select trailing commas; AV uses vertical SELECT columns and EPM
automatic list layout. The mapping preserves source values and explicitly
marks unverified numeric modes/Style as native approximations. This does not certify
SQL Complete output parity or add a generic XML importer. See the
[working mapping](sql-complete-working-mapping.tsv) and
[project audit and generation commands](../../docs/sql-complete-working-profiles.md).

The following SC-26 artifacts remain historical native alternatives; their immutable
source snapshot and coverage ledger are unchanged by the working-profile exporter.

`ReadableVertical.json` and `CompactQueries.json` are original formatter presets, **not conversions of AV_Profile or Right aligned EPM-AWB2** and not promises of identical output. ReadableVertical uses width 100, vertical lists, supported alignment and explicit module boundaries. CompactQueries uses width 120, inline lists and margin-based query compactness; it does not flatten all procedural code. Both retain the Core's safe-layout limitations. Import either file on the IDE's All settings page or copy it to a project's `.tsqlformatter.json` after checking/backing up any existing file. CLI discovers that configuration normally; preset names are not new `--profile` IDs.

The XML profiles are user-supplied requirements references. They are not distributed here. Their normalized, hash-checked snapshot records the union of all 977 scalar paths (577 groups), including the 5 fields absent from AV. Numeric XML modes and Style values have no verified control-output oracle; the SC-26 alternative export therefore takes the explicit fallback instead of shipping a speculative XML converter. That alternative exporter does not interpret numeric modes. This is a technical decision, not a determination of third-party licensing rights.

`sql-complete-correspondence.tsv` gives **one row per original path**, its original type and exact AV/EPM values, coverage status, native setting, effective value of each native alternative, explicit decision, test evidence and documented native semantics. Older mappings may include a member or `:kind:default` metadata. For compound rules a whole native object is shown when the ledger maps to its root; it is not an interpretation of XML Style. `native_alternative_not_xml_conversion` always means “choose/configure the native equivalent yourself”, not “the original value was converted”. Only the 8 already user-approved Subquery OptionHints paths are excluded. All 969 applicable correspondences remain implemented and editable.

From the repository root, with PowerShell 7 and dependencies restored:

```powershell
pwsh -NoProfile -File scripts/Export-NativeProfileAlternatives.ps1
```

The default command checks reproducibility and writes nothing. `-Write` regenerates only these three generated artifacts from the immutable snapshot, coverage ledger and native preset definitions. Neither command reads XML, executes SQL, modifies the snapshot/ledger, nor publishes anything. Tests verify every report row, JSON round trips and both native alternatives over all 20 categories. SC-27 adds 82 golden cases, performance checks and narrow installed-IDE checks; see the [final audit](../../docs/sql-complete-parity-audit.md) for evidence and limitations. Unspecified boundaries/indents retain defaults: these presets do not universally flatten or stack every SQL form.
