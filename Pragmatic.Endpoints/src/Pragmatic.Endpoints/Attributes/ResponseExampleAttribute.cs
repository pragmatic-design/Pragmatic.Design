namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Declares a response body example for a status code in OpenAPI documentation.
///     Repeat the attribute for multiple status codes or multiple named examples.
/// </summary>
/// <remarks>
///     The example is a JSON string (use a raw string literal). It flows through the
///     compile-time manifest into the OpenAPI document; invalid JSON raises PRAG0518.
/// </remarks>
/// <param name="statusCode">The HTTP status code the example documents.</param>
/// <param name="json">The example response body as a JSON string.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ResponseExampleAttribute(int statusCode, string json) : Attribute
{
    /// <summary>The HTTP status code the example documents.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>The example response body as JSON.</summary>
    public string Json { get; } = json;

    /// <summary>Optional example name; required to distinguish multiple examples per status.</summary>
    public string? Name { get; set; }
}
