using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Pragmatic.Endpoints.ApiExplorer;

/// <summary>
///     Whether ASP.NET's OpenAPI document can hold the schema of a type.
/// </summary>
/// <remarks>
///     <para>
///         The document builds a type's schema with <see cref="JsonSchemaExporter" />, which writes every
///         nested type inline and stops only at recursion, and then reads that schema back through a
///         writer limited to the application's <see cref="JsonSerializerOptions.MaxDepth" /> — 64 unless
///         the application changed it. A type graph deeper than that throws, and the exception takes the
///         whole document down with it, not the one operation.
///     </para>
///     <para>
///         ⚠️ Found on the Showcase: mutations that answer with their entity publish the entity type, and
///         an entity's navigations reach 64 levels quickly. Raising <c>MaxDepth</c> is not the answer —
///         it is the same option that bounds how deeply a request body may nest.
///     </para>
///     <para>
///         This measures the schema the way the document is about to build it — same exporter, same
///         options — so it answers the document's question, not an estimate of it. It mitigates a limit
///         of ASP.NET; it does not remove it.
///     </para>
/// </remarks>
internal sealed class SchemaDepthGuard
{
    private readonly JsonSerializerOptions _options;

    // Per type: null when the document can hold the schema, otherwise why it cannot.
    private readonly ConcurrentDictionary<Type, string?> _refusals = new();

    // The document's exporter settings that change the shape of the schema: nullability decides whether
    // "type" is a string or an array, which is not a level of nesting, but it is kept identical anyway.
    private static readonly JsonSchemaExporterOptions Exporter = new() { TreatNullObliviousAsNonNullable = true };

    public SchemaDepthGuard(JsonSerializerOptions applicationOptions)
    {
        _options = new JsonSerializerOptions(applicationOptions);
        Limit = applicationOptions.MaxDepth == 0 ? 64 : applicationOptions.MaxDepth;
    }

    /// <summary>The nesting the document can read back.</summary>
    public int Limit { get; }

    /// <summary>
    ///     Whether the document can hold the schema of <paramref name="type" />, and if not, why.
    /// </summary>
    public bool Fits(Type type, out string? refusal)
    {
        refusal = _refusals.GetOrAdd(type, Measure);
        return refusal is null;
    }

    private string? Measure(Type type)
    {
        JsonNode schema;
        try
        {
            schema = _options.GetTypeInfo(type).GetJsonSchemaAsNode(Exporter);
        }
        catch (InvalidOperationException exporterRefusal)
        {
            // The exporter has its own depth limit and throws past it, before the document's writer is
            // ever reached — and the document calls the same exporter, so it would throw the same way.
            // The message goes into the refusal: a refusal for another reason must not read as depth.
            return $"the JSON schema exporter refuses it ({exporterRefusal.Message})";
        }

        var depth = ResolvedDepth(schema, schema);
        return depth <= Limit
            ? null
            : $"it nests {depth} levels and the document reads back at most {Limit}";
    }

    /// <summary>
    ///     The depth of the schema once the document has resolved its references.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the depth of the exporter's output. The exporter cuts recursion with a <c>$ref</c> to an
    ///     ancestor; the document then replaces each such reference with a deep copy of the subtree it
    ///     points at — without resolving the references inside the copy — before it reads the schema
    ///     back. Measured without that step, this let every Showcase response through and the document
    ///     still failed.
    /// </remarks>
    private static int ResolvedDepth(JsonNode? node, JsonNode root)
    {
        switch (node)
        {
            case JsonObject obj when obj.TryGetPropertyValue("$ref", out var reference)
                                     && reference is JsonValue value
                                     && value.TryGetValue<string>(out var path)
                                     && path.StartsWith('#'):
                return Target(path, root) is { } target ? Depth(target) : Depth(obj);

            case JsonObject obj:
                return 1 + obj.Select(p => ResolvedDepth(p.Value, root)).DefaultIfEmpty(0).Max();

            case JsonArray array:
                return 1 + array.Select(item => ResolvedDepth(item, root)).DefaultIfEmpty(0).Max();

            default:
                return 0;
        }
    }

    /// <summary>The node a fragment reference points at, walked the way the document walks it.</summary>
    private static JsonNode? Target(string path, JsonNode root)
    {
        var current = root;

        foreach (var segment in path.TrimStart('#', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current switch
            {
                JsonObject obj => obj.TryGetPropertyValue(segment, out var next) ? next : null,
                JsonArray array when int.TryParse(segment, out var index) && index >= 0 && index < array.Count => array[index],
                _ => null,
            };

            if (current is null)
                return null;
        }

        return current;
    }

    private static int Depth(JsonNode? node) => node switch
    {
        JsonObject obj => 1 + obj.Select(p => Depth(p.Value)).DefaultIfEmpty(0).Max(),
        JsonArray array => 1 + array.Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0,
    };
}
