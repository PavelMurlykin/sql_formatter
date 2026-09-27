using BenchmarkDotNet.Running;
using TSqlFormatter.Benchmarks;

if (args.Contains("--editor-validation", StringComparer.Ordinal))
{
    EditorWorkflowValidation.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(DocRendererBenchmarks).Assembly).Run(args);
