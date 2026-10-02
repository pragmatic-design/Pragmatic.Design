using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Represents a logical sub-grouping within a boundary, inferred from namespace structure.
///     Sub-boundaries generate their own interface and local implementation, composed into the root via property.
/// </summary>
internal sealed record SubBoundaryModel
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string InterfaceName { get; init; }
    public required string ImplementationName { get; init; }
    public required string PropertyName { get; init; }
    public string? Description { get; init; }
    public required EquatableArray<BoundaryMemberModel> PublicMembers { get; init; }

    /// <summary>
    ///     The group's operations that stay inside the module.
    /// </summary>
    /// <remarks>
    ///     ⚠️ They live on <see cref="InternalInterfaceName" />, which is the seam a caller inside the
    ///     module already holds. Put flat onto the root's internal interface whatever their namespace
    ///     said, the path a caller writes would depend on the operation's <em>visibility</em> —
    ///     something the folder layout does not announce.
    /// </remarks>
    public EquatableArray<BoundaryMemberModel> InternalMembers { get; init; } =
        EquatableArray<BoundaryMemberModel>.Empty;

    /// <summary>
    ///     The internal twin of <see cref="InterfaceName" />, which carries the shapes that cannot
    ///     leave the process.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A sub-boundary has one public interface and the remote implementation implements it, so a
    ///     method taking a tracked entity cannot go there — it would have to be serialised, and what
    ///     came back would not be the row the caller is holding. This twin is what an operation inside
    ///     the module sees, through the root's internal interface, and nothing remote ever implements
    ///     it.
    /// </remarks>
    public string InternalInterfaceName => InterfaceName.EndsWith("Actions", System.StringComparison.Ordinal)
        ? InterfaceName.Substring(0, InterfaceName.Length - "Actions".Length) + "InternalActions"
        : InterfaceName + "Internal";

    /// <summary>Whether any member of this group can be handed a row the caller already holds.</summary>
    public bool HasPreloadedShapes
    {
        get
        {
            foreach (var member in PublicMembers)
                if (member is { IsMutation: true, MutationCreates: false, EntityFullTypeName: not null })
                    return true;

            return false;
        }
    }

    /// <summary>Whether this group needs an internal twin at all.</summary>
    /// <remarks>
    ///     Nothing to put on it, no twin: an empty interface and a property returning it would be
    ///     surface with nothing behind it, in every application that groups its operations. Two things
    ///     can fill it — an operation that stays inside the module, and the preloaded shape of one that
    ///     does not.
    /// </remarks>
    public bool HasInternalTwin => !InternalMembers.IsDefaultOrEmpty || HasPreloadedShapes;
}
