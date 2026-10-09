using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     The reservation page by subtraction (#131): what the generated writer spends on each part of an item,
///     against STJ's fast path in the same run, and what writing the parts the encoder cannot change as raw blocks
///     takes off.
/// </summary>
[MemoryDiagnoser]
public class PageProbeBenchmarks : ResponseProbeHarness<ReservationPage>
{
    private static readonly Action<Utf8JsonWriter, ReservationPage> Plain = PageProbeWriter.Page<PlainProbe>;
    private static readonly Action<Utf8JsonWriter, ReservationPage> NoStrings = PageProbeWriter.Page<ConstantStringsProbe>;
    private static readonly Action<Utf8JsonWriter, ReservationPage> NoDates = PageProbeWriter.Page<ConstantDatesProbe>;
    private static readonly Action<Utf8JsonWriter, ReservationPage> Numbers = PageProbeWriter.Page<RawNumbersProbe>;
    private static readonly Action<Utf8JsonWriter, ReservationPage> EncoderFree = PageProbeWriter.Page<RawEncoderFreeProbe>;

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
                (nameof(Minus_StringValues), Minus_StringValues()),
                (nameof(Minus_DateFormatting), Minus_DateFormatting()),
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
    public byte[] Minus_StringValues() => Run(Writer, NoStrings);

    [Benchmark]
    public byte[] Minus_DateFormatting() => Run(Writer, NoDates);

    [Benchmark]
    public byte[] Raw_Numbers() => Run(Writer, Numbers);

    [Benchmark]
    public byte[] Raw_EncoderFree() => Run(Writer, EncoderFree);
}
