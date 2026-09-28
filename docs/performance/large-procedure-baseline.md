# Large-procedure formatting baseline

Date: 2026-09-28. Platform: local Windows, .NET 10, Release build. This is one
warm-up followed by one timed run per size, not a statistically rigorous
benchmark and not an IDE-host latency measurement.

Reproduce from the repository root:

```powershell
dotnet build benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -c Release
dotnet run --project benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -c Release --no-build -- --large-procedure-validation
```

The generated, valid `CREATE PROCEDURE` has a `DECLARE`, an initial `SET`, the
listed number of `SET @value = @value + 1` statements, and a final `SELECT`.
The command runs the complete `ScriptDomSqlFormatter`, rejects parse/validation
errors, and reports elapsed wall time and allocations on the calling thread.

| Assignments | Characters | Elapsed | Thread allocations |
| ---: | ---: | ---: | ---: |
| 100 | 2,615 | 3.3 ms | 0.78 MiB |
| 500 | 12,615 | 15.6 ms | 3.40 MiB |
| 1,000 | 25,115 | 31.4 ms | 6.70 MiB |

These values are a comparison baseline, not a service-level target. Different
SQL shapes, comments, options, machines, or parser versions may be much slower.
The stricter automatic-editor limits in
[editor workflow validation](editor-workflow-validation.md) remain unchanged.
