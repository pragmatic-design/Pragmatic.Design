namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a claim parameter bound from JWT claims.
/// </summary>
internal sealed record ClaimParameterModel
{
    /// <summary>
    ///     The claim type to bind from (e.g., "sub", "name", "role").
    /// </summary>
    public required string ClaimType { get; init; }

    /// <summary>
    ///     The property name in the endpoint class.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The fully qualified type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the claim is required (returns 401 if missing).
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the type needs conversion from string (true for non-string types like Guid, int).
    /// </summary>
    public bool NeedsConversion { get; init; }

    /// <summary>
    ///     Whether the property has an <c>init</c> accessor, so it can only be set in the object
    ///     initializer.
    /// </summary>
    public bool IsInitOnly { get; init; }

    /// <summary>
    ///     What an optional <c>init</c> property is given when the claim is absent: its declared
    ///     initializer, or <c>default!</c> when it declares none. <c>null</c> when the initializer cannot
    ///     be repeated (PRAG0536).
    /// </summary>
    public string? InitOnlyFallback { get; init; }
}
