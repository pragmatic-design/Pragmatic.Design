using System.Text.Json.Serialization;
using Pragmatic.Endpoints.Responses;

namespace Pragmatic.Endpoints.Serialization;

/// <summary>
///     STJ source-generated context for the fixed wire types of Pragmatic.Endpoints
///     (SSE error events). Keeps the SSE path AOT-safe without the reflection resolver.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SseError))]
public sealed partial class PragmaticEndpointsJsonContext : JsonSerializerContext;
