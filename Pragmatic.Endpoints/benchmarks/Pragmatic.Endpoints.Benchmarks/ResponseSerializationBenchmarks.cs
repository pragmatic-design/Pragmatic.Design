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
///         Every competitor writes into one buffer kept across calls and returns the count it wrote, not an array.
///         A host writes into the response, never into a <c>byte[]</c>; and a fresh array of a large document is a
///         large-object-heap allocation whose cost changed mid-run on the CI runner, which made the rows that
///         returned one step up while the code stayed the same (#150).
///     </para>
///     <para>
///         ⚠️ <see cref="Setup" /> refuses to time anything unless the outputs are the same document: parsed and
///         compared structurally, so key order, number formatting and escaping may differ and nothing else may. A
///         competitor that wrote less (nulls the host leaves out, a member it skipped) would otherwise look faster
///         for doing less.
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
    private Utf8JsonWriter _hostWriter = null!;
    private Utf8JsonWriter _fastPathWriter = null!;
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

        // Serializing into a writer, STJ takes the encoder from the writer and not from the options: each STJ row
        // gets a writer built as STJ builds its own from those options, validation off as in a release build.
        _hostWriter = new Utf8JsonWriter(_buffer, WriterOptionsOf(Competitors.HostReflection));
        _fastPathWriter = new Utf8JsonWriter(_buffer, WriterOptionsOf(_fastPath.Options));

        // As GeneratedJsonResponse writes a response: the encoder the host's options carry, no validation.
        _writer = new Utf8JsonWriter(_buffer, Pragmatic.Serialization.GeneratedJsonDefaults.ResponseWriterOptions);
        _defaultEncoderWriter = new Utf8JsonWriter(_buffer, new JsonWriterOptions { SkipValidation = true });

        VerifySameDocument();
    }

    /// <summary>The host as it is without opting in: its options, the seam, reflection for this type.</summary>
    [Benchmark(Baseline = true)]
    public int Host_Reflection()
    {
        Begin(_hostWriter);
        System.Text.Json.JsonSerializer.Serialize(_hostWriter, _root, _hostReflection);
        return End(_hostWriter);
    }

    /// <summary>The host with generated metadata for the type: what an opted-in host pays today.</summary>
    [Benchmark]
    public int Host_GeneratedMetadata()
    {
        Begin(_hostWriter);
        System.Text.Json.JsonSerializer.Serialize(_hostWriter, _root, _hostGeneratedMetadata);
        return End(_hostWriter);
    }

    /// <summary>STJ's serialization handler, with options compatible with it.</summary>
    [Benchmark]
    public int Stj_FastPath()
    {
        Begin(_fastPathWriter);
        System.Text.Json.JsonSerializer.Serialize(_fastPathWriter, _root, _fastPath);
        return End(_fastPathWriter);
    }

    /// <summary>RE:Dox, configured to the host's contract, into the same buffer through its own writer.</summary>
    [Benchmark]
    public int REDox()
    {
        _buffer.ResetWrittenCount();
        global::REDox.Json.JsonSerializer.Serialize(_buffer, _root, Competitors.REDox);
        return _buffer.WrittenCount;
    }

    /// <summary>
    ///     The generated UTF-8 writer of the type, into a buffer and a writer kept across calls, as a response is
    ///     written.
    /// </summary>
    /// <remarks>
    ///     A workload the generator refused has no writer, and the case reports NA with the reason rather than a
    ///     number for something else.
    /// </remarks>
    [Benchmark]
    public int Generated_Writer()
    {
        var write = _generated
                    ?? throw new NotSupportedException($"The generator wrote no writer for {typeof(T).FullName}; PRAG0555 says why.");

        Begin(_writer);
        write(_writer, _root, Competitors.HostReflection);
        return End(_writer);
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
    public int Generated_Writer_DefaultEncoder()
    {
        var write = _generated
                    ?? throw new NotSupportedException($"The generator wrote no writer for {typeof(T).FullName}; PRAG0555 says why.");

        Begin(_defaultEncoderWriter);
        write(_defaultEncoderWriter, _root, Competitors.HostReflection);
        return End(_defaultEncoderWriter);
    }

    /// <summary>
    ///     Throws unless every competitor wrote the document the host writes, and the generated writer wrote the
    ///     host's very bytes.
    /// </summary>
    /// <remarks>
    ///     The host's bytes are also checked against <c>SerializeToUtf8Bytes</c>, which takes the encoder from the
    ///     options: a writer built with another encoder would pass every structural comparison and time a host that
    ///     does not exist.
    /// </remarks>
    public void VerifySameDocument()
    {
        var host = Written(Host_Reflection);
        if (!host.AsSpan().SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root, _hostReflection)))
            throw new InvalidOperationException(
                $"{nameof(Host_Reflection)} does not write what the host's options write for {typeof(T).FullName}.");

        if (_generated is not null && !Written(Generated_Writer).AsSpan().SequenceEqual(host))
            throw new InvalidOperationException(
                $"{nameof(Generated_Writer)} does not write the host's bytes for {typeof(T).FullName}.");

        var expected = JsonNode.Parse(host);

        foreach (var (name, bytes) in new[]
                 {
                     (nameof(Host_GeneratedMetadata), Written(Host_GeneratedMetadata)),
                     (nameof(Stj_FastPath), Written(Stj_FastPath)),
                     (nameof(REDox), Written(REDox)),
                 })
        {
            if (!JsonNode.DeepEquals(expected, JsonNode.Parse(bytes)))
                throw new InvalidOperationException(
                    $"{name} does not write the document the host writes for {typeof(T).FullName}.");
        }
    }

    private static JsonWriterOptions WriterOptionsOf(JsonSerializerOptions options) => new()
    {
        Encoder = options.Encoder,
        Indented = options.WriteIndented,
        MaxDepth = options.MaxDepth,
        SkipValidation = true,
    };

    private void Begin(Utf8JsonWriter writer)
    {
        _buffer.ResetWrittenCount();
        writer.Reset(_buffer);
    }

    private int End(Utf8JsonWriter writer)
    {
        writer.Flush();
        return _buffer.WrittenCount;
    }

    private byte[] Written(Func<int> write)
    {
        var count = write();
        if (count != _buffer.WrittenCount)
            throw new InvalidOperationException("A competitor reported a count other than what it wrote.");

        return _buffer.WrittenSpan.ToArray();
    }
}
