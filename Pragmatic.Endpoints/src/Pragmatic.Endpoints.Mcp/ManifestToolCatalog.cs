using Pragmatic.Endpoints.OpenApi;

namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Resolves the [McpTool] endpoints from the compile-time manifest into tool descriptors.
///     Tool name = override or sanitized operation id (Booking.CreateReservation →
///     booking_createreservation); collisions get a numeric suffix.
/// </summary>
public sealed class ManifestToolCatalog
{
    private readonly Lazy<IReadOnlyList<McpToolDescriptor>> _tools = new(BuildAll);

    /// <summary>The exposed tools, in stable order.</summary>
    public IReadOnlyList<McpToolDescriptor> Tools => _tools.Value;

    /// <summary>Finds a tool by name, or null.</summary>
    public McpToolDescriptor? Find(string name)
        => Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    private static IReadOnlyList<McpToolDescriptor> BuildAll()
    {
        var tools = new List<McpToolDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var manifest in ManifestReader.ReadAll())
        foreach (var endpoint in manifest.Endpoints ?? [])
        {
            if (endpoint.Mcp is not { Enabled: true } mcp)
                continue;
            if (endpoint.FullRoute is null || endpoint.HttpMethod is null)
                continue;
            if (endpoint.Response?.IsStream == true || endpoint.FileUpload is not null)
                continue; // streams and uploads are not tool-shaped

            var name = Sanitize(mcp.Name ?? endpoint.OperationId ?? endpoint.FullRoute);
            var unique = name;
            var suffix = 2;
            while (!seen.Add(unique))
                unique = $"{name}_{suffix++}";

            tools.Add(new McpToolDescriptor(
                unique,
                mcp.Description ?? endpoint.Summary ?? $"{endpoint.HttpMethod} {endpoint.FullRoute}",
                McpJsonSchemaBuilder.Build(endpoint),
                endpoint.HttpMethod.ToUpperInvariant(),
                endpoint.FullRoute,
                endpoint.Parameters ?? [],
                endpoint.RequestBody?.Properties ?? [],
                endpoint.IdempotencyHeader));
        }

        return tools.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    private static string Sanitize(string value)
    {
        // Operation ids carry the endpoint type name — drop the Endpoint/Action/Mutation/Query
        // suffix so models see "booking_createnote", not "booking_createnoteendpoint".
        foreach (var suffix in new[] { "Endpoint", "Action", "Mutation", "Query" })
            if (value.Length > suffix.Length && value.EndsWith(suffix, StringComparison.Ordinal))
            {
                value = value[..^suffix.Length];
                break;
            }

        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var sanitized = new string(chars).Trim('_');
        while (sanitized.Contains("__", StringComparison.Ordinal))
            sanitized = sanitized.Replace("__", "_");
        return sanitized.Length > 0 ? sanitized : "tool";
    }
}
