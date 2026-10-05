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
    private T _root = default!;
    private JsonTypeInfo<T> _hostReflection = null!;
    private JsonTypeInfo<T> _hostGeneratedMetadata = null!;
    private JsonTypeInfo<T> _fastPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _root = Workloads.Create<T>();
        _hostReflection = (JsonTypeInfo<T>)Competitors.HostReflection.GetTypeInfo(typeof(T));
        _hostGeneratedMetadata = (JsonTypeInfo<T>)Competitors.HostGeneratedMetadata.GetTypeInfo(typeof(T));
        _fastPath = (JsonTypeInfo<T>)FastPathContext.Default.GetTypeInfo(typeof(T))!;

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

    /// <summary>Throws unless every competitor wrote the document the host writes.</summary>
    public void VerifySameDocument()
    {
        var expected = JsonNode.Parse(Host_Reflection());

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
