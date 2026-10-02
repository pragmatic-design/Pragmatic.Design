namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Response body example from [ResponseExample] for OpenAPI documentation.
/// </summary>
internal sealed record ResponseExampleModel
{
    /// <summary>The HTTP status code the example documents.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The example payload as a JSON string.</summary>
    public required string Json { get; init; }

    /// <summary>Optional example name (distinguishes multiple examples per status).</summary>
    public string? Name { get; init; }

    /// <summary>Whether <see cref="Json" /> is structurally valid (PRAG0518 when false).</summary>
    public bool IsValidJson { get; init; }
}
