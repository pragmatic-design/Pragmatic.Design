namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Declares a request body example for OpenAPI documentation. Repeat the attribute for
///     multiple named examples (rendered as a dropdown in Swagger UI).
/// </summary>
/// <remarks>
///     The example is a JSON string (use a raw string literal). It flows through the
///     compile-time manifest into the OpenAPI document; invalid JSON raises PRAG0518.
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/guests")]
/// [RequestExample("""{ "name": "Ada", "email": "ada@example.com" }""", Name = "minimal")]
/// public partial class CreateGuestMutation : Mutation&lt;Guest&gt; { ... }
///     </code>
/// </example>
/// <param name="json">The example request body as a JSON string.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequestExampleAttribute(string json) : Attribute
{
    /// <summary>The example request body as JSON.</summary>
    public string Json { get; } = json;

    /// <summary>Optional example name; required to distinguish multiple examples.</summary>
    public string? Name { get; set; }

    /// <summary>Optional human-readable summary shown next to the example.</summary>
    public string? Summary { get; set; }
}
