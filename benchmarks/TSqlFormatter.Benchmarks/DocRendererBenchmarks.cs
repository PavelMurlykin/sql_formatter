using BenchmarkDotNet.Attributes;
using TSqlFormatter.Core.Layout;

namespace TSqlFormatter.Benchmarks;

[MemoryDiagnoser]
public class DocRendererBenchmarks
{
    private readonly DocRenderer _renderer = new();
    private Doc _small = null!;
    private Doc _medium = null!;
    private Doc _large = null!;
    private DocRenderOptions _flatOptions = null!;
    private DocRenderOptions _wrappedOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = BuildSelect(5);
        _medium = BuildSelect(50);
        _large = BuildSelect(500);
        _flatOptions = new DocRenderOptions(maxLineWidth: 200);
        _wrappedOptions = new DocRenderOptions(maxLineWidth: 80);
    }

    [Benchmark(Baseline = true)]
    public string SmallFlat() => _renderer.Render(_small, _flatOptions);

    [Benchmark]
    public string MediumWrapped() => _renderer.Render(_medium, _wrappedOptions);

    [Benchmark]
    public string LargeWrapped() => _renderer.Render(_large, _wrappedOptions);

    private static Doc BuildSelect(int columnCount)
    {
        var columns = new Doc[columnCount * 2 + 1];
        columns[0] = new TextDoc("SELECT");
        for (var index = 0; index < columnCount; index++)
        {
            columns[index * 2 + 1] = SoftLineDoc.Instance;
            columns[index * 2 + 2] = new IndentDoc(
                1,
                new TextDoc($"Column{index + 1}{(index == columnCount - 1 ? string.Empty : ",")}"));
        }

        return new GroupDoc(new ConcatDoc(columns));
    }
}
