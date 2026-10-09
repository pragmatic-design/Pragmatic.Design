using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Endpoints.Benchmarks.Serialization;

namespace Pragmatic.Endpoints.Benchmarks;

/// <summary>
///     Writing one response body, the same graph through each serializer: the measurement #51 asks for
///     before a generated response writer is built.
/// </summary>
/// <remarks>
///     <para>
///         Every competitor produces UTF-8 bytes from a graph already in memory, as an endpoint writing its
///         result does. Nothing is parsed inside the timed region.
///     </para>
///     <para>
///         ⚠️ <see cref="Setup" /> refuses to time anything unless the four outputs are the same document:
///         parsed and compared structurally, so key order, number formatting and escaping may differ and
///         nothing else may. A competitor that wrote less (nulls the host leaves out, a member it skipped)
///         would otherwise look faster for doing less.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[GenericTypeArguments(typeof(Twitter.Root))]
[GenericTypeArguments(typeof(CitmCatalog.Root))]
[GenericTypeArguments(typeof(Canada.Root))]
[GenericTypeArguments(typeof(ReservationPage))]
public class ResponseSerializationBenchmarks<T>
{
    private readonly ArrayBufferWriter<byte> _buffer = new(64 * 1024);
    private T _root = default!;
    private JsonTypeInfo<T> _hostReflection = null!;
    private JsonTypeInfo<T> _hostGeneratedMetadata = null!;
    private JsonTypeInfo<T> _fastPath = null!;
    private Action<Utf8JsonWriter, T, JsonSerializerOptions>? _generated;
    private Utf8JsonWriter _writer = null!;
    private Utf8JsonWriter _defaultEncoderWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _root = Workloads.Create<T>();
        _hostReflection = (JsonTypeInfo<T>)Competitors.HostReflection.GetTypeInfo(typeof(T));
        _hostGeneratedMetadata = (JsonTypeInfo<T>)Competitors.HostGeneratedMetadata.GetTypeInfo(typeof(T));
        _fastPath = (JsonTypeInfo<T>)FastPathContext.Default.GetTypeInfo(typeof(T))!;
        _generated = GeneratedWriters.For<T>();

        // As GeneratedJsonResponse writes a response: the encoder the host's options carry, no validation.
        _writer = new Utf8JsonWriter(_buffer, new JsonWriterOptions
        {
            Encoder = Pragmatic.Serialization.GeneratedJsonDefaults.ResponseEncoder,
            SkipValidation = true,
        });
        _defaultEncoderWriter = new Utf8JsonWriter(_buffer, new JsonWriterOptions { SkipValidation = true });

        VerifySameDocument();
    }

    /// <summary>The host as it is without opting in: its options, the seam, reflection for this type.</summary>
    [Benchmark(Baseline = true)]
    public byte[] Host_Reflection() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root, _hostReflection);

    /// <summary>The host with generated metadata for the type: what an opted-in host pays today.</summary>
    [Benchmark]
    public byte[] Host_GeneratedMetadata() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root, _hostGeneratedMetadata);

    /// <summary>STJ's serialization handler, with options compatible with it.</summary>
    [Benchmark]
    public byte[] Stj_FastPath() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root, _fastPath);

    /// <summary>RE:Dox, configured to the host's contract.</summary>
    [Benchmark]
    public byte[] REDox() => global::REDox.Json.JsonSerializer.SerializeToUtf8Bytes(_root, Competitors.REDox);

    /// <summary>
    ///     The generated UTF-8 writer of the type, into a buffer and a writer kept across calls, as a response is
    ///     written; copied out at the end, as <c>SerializeToUtf8Bytes</c> copies its pooled buffer.
    /// </summary>
    /// <remarks>
    ///     A workload the generator refused has no writer, and the case reports NA with the reason rather than a
    ///     number for something else.
    /// </remarks>
    [Benchmark]
    public byte[] Generated_Writer()
    {
        var write = _generated
                    ?? throw new NotSupportedException($"The generator wrote no writer for {typeof(T).FullName}; PRAG0555 says why.");

        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        write(_writer, _root, Competitors.HostReflection);
        _writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     The same generated writer with the encoder <see cref="Stj_FastPath" /> writes with: STJ's default, which
    ///     the writer checks through a static table rather than the encoder's virtual method.
    /// </summary>
    /// <remarks>
    ///     Not what a host answers with — its options carry ASP.NET's relaxed encoder, so the bytes differ wherever
    ///     there is non-ASCII text — but the one comparison with the fast path where both escape alike: what the
    ///     writer costs, apart from what the host's encoder costs.
    /// </remarks>
    [Benchmark]
    public byte[] Generated_Writer_DefaultEncoder()
    {
        var write = _generated
                    ?? throw new NotSupportedException($"The generator wrote no writer for {typeof(T).FullName}; PRAG0555 says why.");

        _buffer.ResetWrittenCount();
        _defaultEncoderWriter.Reset(_buffer);
        write(_defaultEncoderWriter, _root, Competitors.HostReflection);
        _defaultEncoderWriter.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Throws unless every competitor wrote the document the host writes, and the generated writer wrote the
    ///     host's very bytes.
    /// </summary>
    public void VerifySameDocument()
    {
        var host = Host_Reflection();
        if (_generated is not null && !Generated_Writer().AsSpan().SequenceEqual(host))
            throw new InvalidOperationException(
                $"{nameof(Generated_Writer)} does not write the host's bytes for {typeof(T).FullName}.");

        var expected = JsonNode.Parse(host);

        foreach (var (name, bytes) in new[]
                 {
                     (nameof(Host_GeneratedMetadata), Host_GeneratedMetadata()),
                     (nameof(Stj_FastPath), Stj_FastPath()),
                     (nameof(REDox), REDox()),
                 })
        {
            if (!JsonNode.DeepEquals(expected, JsonNode.Parse(bytes)))
                throw new InvalidOperationException(
                    $"{name} does not write the document the host writes for {typeof(T).FullName}.");
        }
    }
}
