using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Testing;

/// <summary>
///     Shared JSON options for the typed test client — mirrors the Pragmatic host defaults
///     (camelCase, case-insensitive, enums as strings).
/// </summary>
public static class PragmaticJson
{
    /// <summary>The serializer options used by the generated Api client and ApiResponse.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
