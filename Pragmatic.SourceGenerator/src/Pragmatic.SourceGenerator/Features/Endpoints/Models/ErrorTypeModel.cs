namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing an error type with its HTTP status code mapping.
/// </summary>
internal sealed record ErrorTypeModel
{
    /// <summary>
    ///     The fully qualified type name with global:: prefix.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The simple type name for display.
    /// </summary>
    public required string SimpleName { get; init; }

    /// <summary>
    ///     The HTTP status code this error maps to.
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>
    ///     The status the type itself answers with, when it disagrees with the one it declares with
    ///     <c>[HttpStatus]</c> — null when they agree, or when this compilation cannot read the type's own
    ///     (PRAG0537).
    /// </summary>
    public int? ContradictedStatusCode { get; init; }
}
