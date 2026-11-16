using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     What a view's <c>[GroupBy(IncludeEmpty = true)]</c> cannot answer (#766), reported as <c>PRAG0748</c>, <c>PRAG0749</c> or
///     <c>PRAG0750</c>.
/// </summary>
/// <remarks>
///     Carried rather than silently dropped: an empty group answered with a default is a 0 a caller cannot tell from a real one.
/// </remarks>
internal sealed record EmptyGroupRefusalModel
{
    /// <summary>Which rule the view breaks.</summary>
    public required EmptyGroupRefusalKind Kind { get; init; }

    /// <summary>What the message names: the keys, the key and its type, or the member and its aggregate.</summary>
    public required string Detail { get; init; }

    /// <summary>The attribute or the member the author wrote.</summary>
    public LocationInfo? Location { get; init; }
}
