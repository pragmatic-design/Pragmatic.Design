using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Imaging.Benchmarks.Pipelines;
using Pragmatic.Imaging.Benchmarks.Workload;

namespace Pragmatic.Imaging.Benchmarks;

/// <summary>
///     WebP decode: the managed decoder against the Rust library it replaced (image-webp, through the
///     native pipeline), on a lossy 4 MP photo and a lossless 2 MP image that libwebp wrote.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WebPBenchmarks
{
    private byte[] _lossy = null!;
    private byte[] _lossless = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lossy = Fixtures.WebPLossy4Mp;
        _lossless = Fixtures.WebPLossless2Mp;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Decode WebP lossy 4MP")]
    public int DecodeWebPLossy_Native() => NativePipeline.Decode(_lossy);

    [Benchmark, BenchmarkCategory("Decode WebP lossy 4MP")]
    public int DecodeWebPLossy_Managed() => ManagedPipeline.DecodeWebP(_lossy);

    [Benchmark(Baseline = true), BenchmarkCategory("Decode WebP lossless 2MP")]
    public int DecodeWebPLossless_Native() => NativePipeline.Decode(_lossless);

    [Benchmark, BenchmarkCategory("Decode WebP lossless 2MP")]
    public int DecodeWebPLossless_Managed() => ManagedPipeline.DecodeWebP(_lossless);
}
