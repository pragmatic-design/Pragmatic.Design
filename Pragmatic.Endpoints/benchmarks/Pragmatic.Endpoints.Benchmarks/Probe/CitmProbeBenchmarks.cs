using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     <c>citm_catalog.json</c> by subtraction (#131): what the generated writer spends on each part of the
///     document, against STJ's fast path in the same run, and what writing the parts the encoder cannot change as
///     raw blocks takes off.
/// </summary>
[MemoryDiagnoser]
public class CitmProbeBenchmarks : ResponseProbeHarness<CitmCatalog.Root>
{
    private static readonly Action<Utf8JsonWriter, CitmCatalog.Root> Plain = CitmProbeWriter.Root<PlainProbe>;
    private static readonly Action<Utf8JsonWriter, CitmCatalog.Root> NoKeys = CitmProbeWriter.Root<ConstantKeysProbe>;
    private static readonly Action<Utf8JsonWriter, CitmCatalog.Root> NoStrings = CitmProbeWriter.Root<ConstantStringsProbe>;
    private static readonly Action<Utf8JsonWriter, CitmCatalog.Root> Numbers = CitmProbeWriter.Root<RawNumbersProbe>;
    private static readonly Action<Utf8JsonWriter, CitmCatalog.Root> EncoderFree = CitmProbeWriter.Root<RawEncoderFreeProbe>;

    [GlobalSetup]
    public void Setup()
    {
        SetUp();
        Verify(
            [
                (nameof(Generated_Writer), Generated_Writer()),
                (nameof(Probe_Plain), Probe_Plain()),
                (nameof(Raw_Numbers), Raw_Numbers()),
                (nameof(Raw_EncoderFree), Raw_EncoderFree()),
            ],
            [
                (nameof(Minus_KeyFormatting), Minus_KeyFormatting()),
                (nameof(Minus_StringValues), Minus_StringValues()),
                (nameof(Probe_Plain_DefaultEncoder), Probe_Plain_DefaultEncoder()),
            ]);
    }

    [Benchmark]
    public byte[] Stj_FastPath() => FastPath();

    [Benchmark]
    public byte[] Generated_Writer() => Generated();

    /// <summary>The generated writer's calls, written out by hand: the row every other one is read against.</summary>
    [Benchmark(Baseline = true)]
    public byte[] Probe_Plain() => Run(Writer, Plain);

    [Benchmark]
    public byte[] Probe_Plain_DefaultEncoder() => Run(DefaultEncoderWriter, Plain);

    [Benchmark]
    public byte[] Minus_KeyFormatting() => Run(Writer, NoKeys);

    [Benchmark]
    public byte[] Minus_StringValues() => Run(Writer, NoStrings);

    [Benchmark]
    public byte[] Raw_Numbers() => Run(Writer, Numbers);

    [Benchmark]
    public byte[] Raw_EncoderFree() => Run(Writer, EncoderFree);
}
