using Microsoft.OpenApi;
#if NET11_0_OR_GREATER
using Map = System.Collections.Generic.Dictionary<string, Microsoft.OpenApi.IOpenApiMediaType>;
#else
using Map = System.Collections.Generic.Dictionary<string, Microsoft.OpenApi.OpenApiMediaType>;
#endif

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     A response's content map, in the type each target's Microsoft.OpenApi declares (#832).
/// </summary>
/// <remarks>
///     Microsoft.OpenApi 3, which ASP.NET Core 11 brings, types the map by <c>IOpenApiMediaType</c>; the net10.0 asset
///     keeps Microsoft.OpenApi 2, where it is the concrete <c>OpenApiMediaType</c>. Internal, so the public API is the
///     same on both targets.
/// </remarks>
internal static class MediaTypeMap
{
    public static Map Empty() => new();

    public static Map Of(string mediaType, OpenApiMediaType media) => new() { [mediaType] = media };
}
