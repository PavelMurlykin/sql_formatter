# Valid T-SQL regression corpus

These anonymized, representative SQL workloads exercise common reporting, DML,
windowing, stored-code, and deliberately unsupported formatting shapes. They are
not production queries and contain no credentials or private data. Keep each file
valid under ScriptDom's current automatic dialect. The corpus test checks parse
success, idempotence, token preservation, and exact string/comment text.

When adding a formatter regression, first add the smallest reproducer as a unit
test; then add a representative script here if the issue affects a broader shape.
