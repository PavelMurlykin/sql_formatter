using BenchmarkDotNet.Running;
using TSqlFormatter.Benchmarks;

if (args.Contains("--editor-validation", StringComparer.Ordinal))
{
    EditorWorkflowValidation.Run();
    return;
}

if (args.Contains("--large-procedure-validation", StringComparer.Ordinal))
{
    LargeProcedureValidation.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(DocRendererBenchmarks).Assembly).Run(args);
