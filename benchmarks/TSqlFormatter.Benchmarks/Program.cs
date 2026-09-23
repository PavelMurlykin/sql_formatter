using BenchmarkDotNet.Running;
using TSqlFormatter.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(DocRendererBenchmarks).Assembly).Run(args);
