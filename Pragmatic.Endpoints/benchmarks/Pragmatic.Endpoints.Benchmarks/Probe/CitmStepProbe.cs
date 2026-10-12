using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Endpoints.Benchmarks.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     The rows of #150: <c>Generated_Writer</c> on <c>citm_catalog.json</c> steps up mid-run on the CI runner while
///     <c>Stj_FastPath</c> stays flat. Each row changes one thing about how the writer's output is held, so the row
///     whose step disappears names the cause.
/// </summary>
/// <remarks>
///     <para>
///         <c>Generated_Writer</c> is the row as <c>ResponseSerializationBenchmarks</c> runs it: a 64 KB
///         <see cref="ArrayBufferWriter{T}" /> kept across calls, grown to the document once, and a <c>ToArray()</c>
///         copy of the ~480 KB result at the end of every call. That copy lands on the large object heap, as the
///         array <c>SerializeToUtf8Bytes</c> returns does for the fast path.
///     </para>
///     <para>
///         Concrete in two classes, one per job, because the allocation baseline is keyed by class and method, not
///         by job: one with tiered compilation as it is, one with it off.
///     </para>
/// </remarks>
[MemoryDiagnoser]
public abstract class CitmStepProbe
{
    private readonly ArrayBufferWriter<byte> _kept = new(64 * 1024);
    private readonly RentedBufferWriter _rented = new(16 * 1024);
    private CitmCatalog.Root _root = null!;
    private JsonTypeInfo<CitmCatalog.Root> _fastPath = null!;
    private Action<Utf8JsonWriter, CitmCatalog.Root, JsonSerializerOptions> _generated = null!;
    private Utf8JsonWriter _keptWriter = null!;
    private Utf8JsonWriter _rentedWriter = null!;

    private static JsonWriterOptions ResponseOptions => new()
    {
        Encoder = Pragmatic.Serialization.GeneratedJsonDefaults.ResponseEncoder,
        SkipValidation = true,
    };

    [GlobalSetup]
    public void Setup()
    {
        _root = Workloads.Create<CitmCatalog.Root>();
        _fastPath = (JsonTypeInfo<CitmCatalog.Root>)FastPathContext.Default.GetTypeInfo(typeof(CitmCatalog.Root))!;
        _generated = GeneratedWriters.For<CitmCatalog.Root>()
                     ?? throw new InvalidOperationException("The generator wrote no writer for CitmCatalog.Root.");
        _keptWriter = new Utf8JsonWriter(_kept, ResponseOptions);
        _rentedWriter = new Utf8JsonWriter(Stream.Null, ResponseOptions);

        var expected = Generated_Writer();
        foreach (var (name, bytes) in new[]
                 {
                     (nameof(Generated_Writer_FreshBuffer), Generated_Writer_FreshBuffer()),
                     (nameof(Generated_Writer_Rented), Generated_Writer_Rented()),
                 })
        {
            if (!bytes.AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException($"{name} does not write what {nameof(Generated_Writer)} writes.");
        }

        if (Generated_Writer_NoCopy() != expected.Length)
            throw new InvalidOperationException($"{nameof(Generated_Writer_NoCopy)} does not write the document.");
    }

    [Benchmark]
    public byte[] Stj_FastPath() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root, _fastPath);

    /// <summary>As <c>ResponseSerializationBenchmarks</c> runs it: the buffer kept across calls, the result copied out.</summary>
    [Benchmark(Baseline = true)]
    public byte[] Generated_Writer()
    {
        WriteKept();
        return _kept.WrittenSpan.ToArray();
    }

    /// <summary>The kept buffer without the copy: no large array per call.</summary>
    [Benchmark]
    public int Generated_Writer_NoCopy()
    {
        WriteKept();
        return _kept.WrittenCount;
    }

    /// <summary>A new buffer and writer every call, grown from 64 KB each time, then copied out.</summary>
    [Benchmark]
    public byte[] Generated_Writer_FreshBuffer()
    {
        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        using var writer = new Utf8JsonWriter(buffer, ResponseOptions);
        _generated(writer, _root, Competitors.HostReflection);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A pooled buffer rented small and grown, as <c>SerializeToUtf8Bytes</c> writes, then copied out.</summary>
    [Benchmark]
    public byte[] Generated_Writer_Rented()
    {
        _rented.Reset(16 * 1024);
        _rentedWriter.Reset(_rented);
        _generated(_rentedWriter, _root, Competitors.HostReflection);
        _rentedWriter.Flush();
        return _rented.WrittenSpan.ToArray();
    }

    private void WriteKept()
    {
        _kept.ResetWrittenCount();
        _keptWriter.Reset(_kept);
        _generated(_keptWriter, _root, Competitors.HostReflection);
        _keptWriter.Flush();
    }
}
