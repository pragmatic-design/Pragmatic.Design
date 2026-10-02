namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Derives a default OpenAPI tag from the endpoint route when no explicit [Tags] is specified.
///     Picks the first meaningful route segment (skipping api/, version prefixes, and action segments).
/// </summary>
internal static class EndpointTagHelper
{
    private static readonly HashSet<string> SkipSegments =
        ["api", "v1", "v2", "v3", "autocomplete", "grid", "search", "devexpress"];

    /// <summary>
    ///     Derives an OpenAPI tag from a route like "api/v1/room-types/{id}/restore" → "Room Types".
    ///     Falls back to entity type name when the route is relative (e.g., "/{id}/confirm").
    /// </summary>
    internal static string? DeriveTagFromRoute(string? route, string? entityTypeName = null)
    {
        // Prefer entity type name — consistent PascalCase, no casing mismatch
        if (!string.IsNullOrEmpty(entityTypeName))
            return PluralizeSimple(entityTypeName!);

        // Fallback: derive from route segment
        if (!string.IsNullOrEmpty(route))
        {
            foreach (var segment in route!.Split('/'))
            {
                if (segment.Length == 0 || segment[0] == '{')
                    continue;

                if (SkipSegments.Contains(segment))
                    continue;

                // Convert kebab-case to Title Case: "room-types" → "Room Types"
                var parts = segment.Split('-');
                return string.Join(" ", parts.Select(static p =>
                    char.ToUpperInvariant(p[0]) + p.Substring(1)));
            }
        }

        return null;
    }

    /// <summary>
    ///     Naive pluralization for entity names. Covers common cases.
    ///     User can override with explicit [Tags] for edge cases.
    /// </summary>
    private static string PluralizeSimple(string name)
    {
        if (name.EndsWith("y", StringComparison.Ordinal) && !name.EndsWith("ay", StringComparison.Ordinal)
                                                          && !name.EndsWith("ey", StringComparison.Ordinal)
                                                          && !name.EndsWith("oy", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 1) + "ies";

        if (name.EndsWith("s", StringComparison.Ordinal) || name.EndsWith("x", StringComparison.Ordinal)
                                                          || name.EndsWith("ch", StringComparison.Ordinal)
                                                          || name.EndsWith("sh", StringComparison.Ordinal))
            return name + "es";

        return name + "s";
    }
}
