using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Temporal.Models;

/// <summary>
///     A DTO property annotated with a timezone conversion attribute
///     ([AsUtc], [From/ToClientTimezone], [From/ToBusinessTimezone], [KeepTimezone]).
///     Flat and value-equatable for incremental caching.
/// </summary>
internal sealed record TemporalBehaviorPropertyModel
{
    /// <summary>Fully qualified containing type (no global:: prefix), e.g. "App.Dtos.OrderResponse".</summary>
    public required string ContainingTypeFqn { get; init; }

    /// <summary>Containing namespace, used to derive the assembly prefix. Empty for global namespace.</summary>
    public required string ContainingNamespace { get; init; }

    /// <summary>The CLR property name.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The TemporalJsonBehavior enum member name (e.g. "ToClientTimezone").</summary>
    public required string Behavior { get; init; }

    /// <summary>Whether the property type is DateTimeOffset/DateTime (or their nullable forms).</summary>
    public required bool IsSupportedPropertyType { get; init; }

    /// <summary>Display string of the property type, for the PRAG0905 message.</summary>
    public required string PropertyTypeDisplay { get; init; }

    /// <summary>
    ///     Where the declaration is, so <c>PRAG0905</c> can point at it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <see cref="LocationInfo"/> and not a <c>Location</c>: the latter holds its syntax
    ///     tree, and a cached model outliving its compilation makes Roslyn throw when it filters
    ///     suppressions. It is also excluded from equality, so carrying it does not defeat the
    ///     incremental cache — a position never changes the generated output.
    /// </remarks>
    public LocationInfo? Location { get; init; }
}
