namespace Pragmatic.Testing;

/// <summary>
///     Response wrapper returned by the generated typed test client for void endpoints.
///     Exposes the raw <see cref="HttpResponseMessage" /> so all existing assertions
///     (<see cref="PragmaticHttpAssertions" />) keep working.
/// </summary>
public class ApiResponse(HttpResponseMessage raw)
{
    /// <summary>The raw HTTP response.</summary>
    public HttpResponseMessage Raw { get; } = raw;

    /// <summary>The response status code.</summary>
    public System.Net.HttpStatusCode StatusCode => Raw.StatusCode;

    /// <summary>Whether the status code indicates success (2xx).</summary>
    public bool IsSuccess => Raw.IsSuccessStatusCode;

    /// <summary>Reads the response body as a string.</summary>
    public Task<string> ReadBodyAsync() => Raw.Content.ReadAsStringAsync();

    /// <summary>Implicit access to the raw response for assertion extensions.</summary>
    public static implicit operator HttpResponseMessage(ApiResponse response) => response.Raw;
}
