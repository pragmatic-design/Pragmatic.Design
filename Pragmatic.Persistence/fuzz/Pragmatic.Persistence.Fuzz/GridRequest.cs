using System.Text.Json;

namespace Pragmatic.Persistence.Fuzz;

/// <summary>
///     The bytes of a request body, bound as an endpoint binds it. A body the binding refuses never reaches an
///     adapter: that refusal is the framework's, and declared.
/// </summary>
internal static class GridRequest
{
    public static T? Bind<T>(ReadOnlySpan<byte> body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
