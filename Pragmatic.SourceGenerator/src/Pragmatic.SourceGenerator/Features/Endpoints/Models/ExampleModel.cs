namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Request body example from [RequestExample] for OpenAPI documentation.
/// </summary>
internal sealed record ExampleModel
{
    /// <summary>The example payload as a JSON string.</summary>
    public required string Json { get; init; }

    /// <summary>Optional example name (distinguishes multiple examples).</summary>
    public string? Name { get; init; }

    /// <summary>Optional human-readable summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Whether <see cref="Json" /> is structurally valid (PRAG0518 when false).</summary>
    public bool IsValidJson { get; init; }
}
