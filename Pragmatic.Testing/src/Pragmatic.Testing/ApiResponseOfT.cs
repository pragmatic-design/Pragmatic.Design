using System.Text.Json;

namespace Pragmatic.Testing;

/// <summary>
///     Typed response wrapper returned by the generated test client: the raw response plus
///     lazy JSON deserialization of the success payload.
/// </summary>
/// <typeparam name="T">The endpoint's response type.</typeparam>
public sealed class ApiResponse<T>(HttpResponseMessage raw) : ApiResponse(raw)
{
    /// <summary>
    ///     Deserializes the response body as <typeparamref name="T" /> (host JSON conventions).
    ///     Throws when the body is empty or not valid JSON for <typeparamref name="T" />.
    /// </summary>
    public async Task<T> ReadAsync()
    {
        var json = await Raw.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, PragmaticJson.Options)
               ?? throw new PragmaticTestAssertionException(
                   $"Response body could not be deserialized as {typeof(T).Name}: '{json}'.");
    }
}
