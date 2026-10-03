using BenchmarkDotNet.Running;
using TSqlFormatter.Benchmarks;

if (args.FirstOrDefault() == "--sql-complete-profiles")
{
    SqlCompleteWorkingProfileExport.Run(args.Skip(1).ToArray());
    return;
}

if (args.FirstOrDefault() == "--sql-prompt-validation")
{
    SqlPromptCorpusValidation.Run(args.Skip(1).ToArray());
    return;
}

if (args.Contains("--parity-validation", StringComparer.Ordinal))
{
    ParityWorkflowValidation.Run();
    return;
}

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
