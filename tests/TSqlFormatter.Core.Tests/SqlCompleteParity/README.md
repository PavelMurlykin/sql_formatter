# SQL Complete profile requirements snapshot (SC-01)

`profile-values.tsv` is an immutable, deterministic audit snapshot of the two user-supplied formatting profiles. It contains only option identifiers, `PropertyValue`/`SubOptions` membership, and Boolean/integer values. `coverage.tsv` is the separate, mutable ledger assigning every path to an implementation stage and an explicit status (`pending_semantics`, `covered`, or `not_applicable`). The source XML contained no free-form values, SQL text, connection information, or credentials; the original XML is not copied into the repository. These files are **not** an application configuration format or a SQL Complete importer.

Source profile SHA-256 values:

- AV: `B8EDF77E135AD48D5206C84594B448A30D5083C9D07637985707FB9AEF88F493`
- Right-aligned EPM-AWB2: `B62A7A7BBC7994E827217DAAB440C65FB4BDAAF994CFD2371A5CCA31C01AE5EE`

The normalized `profile-values.tsv` has SHA-256 `51FC02E8E1AD6CB0487AA4B419BC543560DD2A4D93A7F73B03ED904581B66829`; CI pins this digest so values cannot drift without an explicit review.

The `AV`/`EPM` columns contain the original scalar values; `-` means the nested field is absent in that profile. In the coverage ledger, `NativeSetting`, `Evidence`, and `Semantics` are `-` until the assigned stage establishes the meaning and implements/tests a corresponding native option. `Stage` is not a claim of current support. Never change `Status` to `covered` on the strength of a JSON field alone: it must influence output for an applicable example. `not_applicable` requires evidence that the corresponding T-SQL form is invalid and an explicit user decision; its `NativeSetting` remains `-`. The eight SC-12 `Subquery_OptionHints_*` paths have this classification because `OPTION (...)` is invalid inside scalar/derived subqueries and CTE definitions.

To check this snapshot against the original user-owned XML files when they are available:

```powershell
./scripts/Sync-SqlCompleteParityInventory.ps1 -AvProfile 'path/to/1-AV_Profile.xml' -EpmProfile 'path/to/2-Right-aligned-EPM-AWB2.xml'
```

The script rejects different source hashes, unexpected XML structure or values, duplicate names, changed counts, and any mismatch with `profile-values.tsv` or path/stage mismatch in `coverage.tsv`. `-Write` initializes both files only when the coverage ledger does not yet exist; it refuses to reset progress. Regular CI tests validate the committed snapshot, ledger, and 20-category `corpus.tsv` without requiring the private attachments.

`corpus.tsv` supplies one minimal T-SQL input per top-level profile category. They are starting points for future golden tests, not assertions of formatting parity or SQL Complete output. Some categories still have no structural formatter rule.
