using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     <c>canada.json</c> by subtraction (#131): whether a run helps a document that is almost all floating-point
///     numbers, against STJ's fast path in the same run, and what formatting the numbers costs on its own.
/// </summary>
[MemoryDiagnoser]
public class CanadaProbeBenchmarks : ResponseProbeHarness<Canada.Root>
{
    private static readonly Action<Utf8JsonWriter, Canada.Root> PlainWriter = CanadaProbeWriter.Plain;
    private static readonly Action<Utf8JsonWriter, Canada.Root> RunWriter = CanadaProbeWriter.Run;
    private static readonly Action<Utf8JsonWriter, Canada.Root> NoNumbersWriter = CanadaProbeWriter.ConstantNumbers;

    [GlobalSetup]
    public void Setup()
    {
        SetUp();
        Verify(
            [
                (nameof(Generated_Writer), Generated_Writer()),
                (nameof(Probe_Plain), Probe_Plain()),
                (nameof(Probe_Run), Probe_Run()),
                (nameof(Generated_Writer_Rented), Generated_Writer_Rented()),
            ],
            [(nameof(Minus_NumberFormatting), Minus_NumberFormatting())]);
    }

    [Benchmark]
    public byte[] Stj_FastPath() => FastPath();

    [Benchmark]
    public byte[] Generated_Writer() => Generated();

    /// <summary>Every number through the writer: what the generated writer wrote before runs.</summary>
    [Benchmark(Baseline = true)]
    public byte[] Probe_Plain() => Run(Writer, PlainWriter);

    [Benchmark]
    public byte[] Probe_Run() => Run(Writer, RunWriter);

    [Benchmark]
    public byte[] Minus_NumberFormatting() => Run(Writer, NoNumbersWriter);

    /// <summary>
    ///     The generated writer into a buffer rented small and grown, as <c>SerializeToUtf8Bytes</c> writes: what the
    ///     buffer the harness keeps across calls costs, apart from the writer.
    /// </summary>
    [Benchmark]
    public byte[] Generated_Writer_Rented()
    {
        _rented.Reset(16 * 1024);
        _rentedWriter.Reset(_rented);
        Pragmatic.Endpoints.Benchmarks.Serialization.GeneratedWriters.For<Canada.Root>()!(_rentedWriter, Root);
        _rentedWriter.Flush();
        return _rented.WrittenSpan.ToArray();
    }

    private readonly RentedBufferWriter _rented = new(16 * 1024);
    private readonly Utf8JsonWriter _rentedWriter = new(Stream.Null, new JsonWriterOptions
    {
        Encoder = Pragmatic.Serialization.GeneratedJsonDefaults.ResponseEncoder,
        SkipValidation = true,
    });
}
