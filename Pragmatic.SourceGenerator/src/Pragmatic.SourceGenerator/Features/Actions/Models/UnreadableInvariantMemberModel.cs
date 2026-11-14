using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     A name an <c>[Invariant]</c> gives as its <c>Member</c> or one of its <c>Parameters</c> that the
///     refusal cannot report, with the reason — the payload of PRAG0471.
/// </summary>
/// <remarks>
///     Value-equatable, with the location as <see cref="LocationInfo" />, for the same reason as
///     <see cref="UncallableInvariantModel" />: it travels inside the mutation's model through the
///     incremental pipeline.
/// </remarks>
internal sealed record UnreadableInvariantMemberModel
{
    /// <summary>The invariant method whose attribute names the member.</summary>
    public required string MethodName { get; init; }

    /// <summary>The name as the attribute gives it.</summary>
    public required string MemberName { get; init; }

    /// <summary>Why it cannot be reported, in the words an author would use about their own entity.</summary>
    public required string Reason { get; init; }

    /// <summary>Where the invariant is declared.</summary>
    public LocationInfo? Location { get; init; }
}
