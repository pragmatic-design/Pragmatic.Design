using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Endpoints.Benchmarks.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     What every row of a response probe shares: the workload, STJ's fast path, the generated writer, and a probe
///     writer run as <c>Generated_Writer</c> runs it — through a delegate, into a buffer and a writer kept across
///     calls, copied out at the end.
/// </summary>
/// <remarks>
///     ⚠️ <see cref="Verify" /> refuses to time a candidate that does not write the host's very bytes. A subtraction
///     row writes another document on purpose, and is only checked to write something.
/// </remarks>
public abstract class ResponseProbeHarness<T>
{
    private readonly ArrayBufferWriter<byte> _buffer = new(64 * 1024);
    private JsonTypeInfo<T> _hostReflection = null!;
    private JsonTypeInfo<T> _fastPath = null!;
    private Action<Utf8JsonWriter, T> _generated = null!;

    protected T Root { get; private set; } = default!;

    protected Utf8JsonWriter Writer { get; private set; } = null!;

    protected Utf8JsonWriter DefaultEncoderWriter { get; private set; } = null!;

    protected void SetUp()
    {
        Root = Workloads.Create<T>();
        _hostReflection = (JsonTypeInfo<T>)Competitors.HostReflection.GetTypeInfo(typeof(T));
        _fastPath = (JsonTypeInfo<T>)FastPathContext.Default.GetTypeInfo(typeof(T))!;
        _generated = GeneratedWriters.For<T>()
                     ?? throw new InvalidOperationException($"The generator wrote no writer for {typeof(T).FullName}.");
        Writer = new Utf8JsonWriter(_buffer, new JsonWriterOptions
        {
            Encoder = Pragmatic.Serialization.GeneratedJsonDefaults.ResponseEncoder,
            SkipValidation = true,
        });
        DefaultEncoderWriter = new Utf8JsonWriter(_buffer, new JsonWriterOptions { SkipValidation = true });
    }

    protected byte[] FastPath() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Root, _fastPath);

    protected byte[] Generated() => Run(Writer, _generated);

    protected byte[] Run(Utf8JsonWriter writer, Action<Utf8JsonWriter, T> write)
    {
        _buffer.ResetWrittenCount();
        writer.Reset(_buffer);
        write(writer, Root);
        writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    /// <summary>Throws unless every candidate wrote the host's bytes and every subtraction wrote a document.</summary>
    protected void Verify(IEnumerable<(string Name, byte[] Bytes)> candidates, IEnumerable<(string Name, byte[] Bytes)> subtractions)
    {
        var host = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Root, _hostReflection);
        foreach (var (name, bytes) in candidates)
        {
            if (!bytes.AsSpan().SequenceEqual(host))
                throw new InvalidOperationException($"{name} does not write the host's bytes for {typeof(T).FullName}.");
        }

        foreach (var (name, bytes) in subtractions)
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"{name} does not write an object for {typeof(T).FullName}.");
        }
    }
}
