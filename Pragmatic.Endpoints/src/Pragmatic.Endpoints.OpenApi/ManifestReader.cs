using System.Text.Json;
using Pragmatic.Endpoints.Manifest;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     Reads the SG-generated PragmaticManifest JSON.
///     Uses the zero-reflection registry populated by SG-generated module initializers.
/// </summary>
public sealed class ManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Reads all manifests from the SG-generated registry.
    /// </summary>
    public static IReadOnlyList<ManifestDocument> ReadAll()
        => Parse(ManifestRegistry.GetAll());

    /// <summary>
    ///     The manifests of <b>this host</b>: its own if it registered one, and otherwise everything the
    ///     process has.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The container first, and the order matters. The registry accumulates
    ///     across the process, so two hosts loaded together share both manifests and each one's
    ///     enrichment is built from the union — which decides, among other things, whether an anonymous
    ///     host publishes security schemes for the other host's operations. The fallback stays because a
    ///     host that registers nothing must keep reading what the process has.
    /// </remarks>
    public static IReadOnlyList<ManifestDocument> ReadFor(IServiceProvider? services)
    {
        var registered = services?.GetService(typeof(HostManifest)) as HostManifest;

        return registered is not null ? Parse([registered.Json]) : ReadAll();
    }

    /// <remarks>
    ///     A module registers its own manifest, and a Composition host registers an aggregated one that
    ///     embeds the same module manifests verbatim. Both can be loaded in one process, so each module
    ///     manifest is read once: identity is the exact text, which is what the host copied. The
    ///     <c>assembly</c> label is not a key — it is derived from a namespace and two assemblies can
    ///     share it.
    /// </remarks>
    internal static IReadOnlyList<ManifestDocument> Parse(IEnumerable<string> jsons)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var manifests = new List<ManifestDocument>();

        foreach (var json in jsons)
        foreach (var module in ModulesOf(json))
        {
            if (!seen.Add(module))
                continue;

            var doc = JsonSerializer.Deserialize<ManifestDocument>(module, JsonOptions);
            if (doc is not null)
                manifests.Add(doc);
        }

        return manifests;
    }

    /// <summary>
    ///     The module manifests a registered document holds: the ones an aggregated host document embeds,
    ///     flattened so consumers always see per-module documents, or the document itself.
    /// </summary>
    private static List<string> ModulesOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("modules", out var modules)
            && modules is { ValueKind: JsonValueKind.Array }
            && modules.GetArrayLength() > 0)
            return modules.EnumerateArray()
                .Where(static m => m.ValueKind == JsonValueKind.Object)
                .Select(static m => m.GetRawText())
                .ToList();

        return [json];
    }
}
