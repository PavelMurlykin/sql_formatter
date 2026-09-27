# Editor workflow performance and cancellation validation

Date: 2026-09-27. Platform: local Windows, .NET 8, Release build. This is a single smoke measurement, not a statistically rigorous benchmark or a Visual Studio host test.

Reproduce from the repository root:

```powershell
dotnet build benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -c Release
dotnet run --project benchmarks/TSqlFormatter.Benchmarks/TSqlFormatter.Benchmarks.csproj -c Release --no-build -- --editor-validation
```

The workload repeats `select Id, Name from dbo.Items where Id = 1 and Name <> 'x';` as separate statements and runs the complete `ScriptDomSqlFormatter` in the calling thread. Observed values:

| Statements | Characters | Elapsed | Thread allocations |
| ---: | ---: | ---: | ---: |
| 10 | 610 | 12.1 ms | 0.47 MiB |
| 100 | 6,100 | 55.2 ms | 13.92 MiB |
| 1,000 | 61,000 | 1,593.5 ms | 1,147.80 MiB |

The 61,000-character case is too costly for an automatic editor action. Accordingly, experimental Format on Save and Paste Formatted SQL are capped at 8,192 UTF-16 characters and use a two-second cooperative cancellation token. Manual formatting retains its existing 16 Mi-character input limit. The cap is based on this synthetic sample, not a guaranteed latency across SQL inputs or machines.

The validation command verifies that a pre-canceled formatter request throws before returning a result. Unit tests additionally cover pre-canceled formatting, renderer cancellation, and CLI cancellation without file replacement. ScriptDom parsing itself is synchronous and may not stop until that call finishes; the two-second token is not a strict wall-clock timeout. Installation-time UI responsiveness, cancellation, Undo, and save behavior still need manual host testing.
