using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A <c>[GroupBy]</c> key, or the <c>Via</c> that reaches it, that names no member of the entity —
///     reported as <c>PRAG0732</c>.
/// </summary>
/// <remarks>
///     Carried rather than skipped: a key the view cannot build is a view that adds up across it.
/// </remarks>
internal sealed record UnresolvedGroupKeyModel
{
    /// <summary>What was named, as the message says it: <c>key 'X'</c> or <c>Via 'Y'</c>.</summary>
    public required string Named { get; init; }

    /// <summary>The entity it was looked up on, fully qualified.</summary>
    public required string EntityType { get; init; }

    /// <summary>The <c>[GroupBy]</c> attribute that named it.</summary>
    public LocationInfo? Location { get; init; }
}
