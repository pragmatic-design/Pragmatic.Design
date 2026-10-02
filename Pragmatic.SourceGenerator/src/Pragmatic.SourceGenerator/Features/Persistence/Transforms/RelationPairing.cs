using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Which two <c>[Relation.*]</c> declarations are the same relationship, and what it resolves to.
/// </summary>
/// <remarks>
///     <para>
///         The answer has to be the same whichever end asks, because both ends emit members from it:
///         the parent's <c>OneToMany</c> writes the child's foreign key and reference navigation, and
///         the child's <c>ManyToOne</c> writes them too. While each derived its own answer they could
///         disagree, and the disagreement was settled by member name — the wrong key, since a
///         relationship's identity is the pair of entities, not the name of a member.
///     </para>
///     <para>
///         Everything needed is available wherever this runs: the graph is built over the referenced
///         modules merged with the current compilation, and a module publishes its raw
///         <c>relationAttributes</c> alongside its resolved navigations, so a declaration in another
///         assembly is readable here.
///     </para>
/// </remarks>
internal static class RelationPairing
{
    /// <summary>
    ///     The declaration on <paramref name="other" /> that describes the same relationship as
    ///     <paramref name="mine" />, or <c>null</c> when this end is the only one that declares it.
    /// </summary>
    /// <param name="other">The entity at the other end, when it is known.</param>
    /// <param name="thisEntityFullName">The entity that declares <paramref name="mine" />.</param>
    /// <param name="wantedKind">The counterpart's relation type: the opposite of <paramref name="mine" />.</param>
    /// <param name="mine">The declaration looking for its other half.</param>
    public static RelationAttributeModel? Counterpart(
        EntityMetadataModel? other,
        string thisEntityFullName,
        string wantedKind,
        RelationAttributeModel mine)
    {
        if (other is null)
            return null;

        var candidates = other.RelationAttributes
            .Where(r => r.RelationType == wantedKind
                        && string.Equals(r.TargetTypeFullName, thisEntityFullName, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        // Several relationships between the same two entities: only the written names tell them
        // apart, and PRAG0612 and PRAG0617 are what make those names mandatory here. Without a name
        // to match on there is no honest pairing, so this end resolves alone.
        foreach (var candidate in candidates)
            if (NameEachOther(mine, candidate))
                return candidate;

        return null;
    }

    /// <summary>Whether either declaration names the other's navigation.</summary>
    private static bool NameEachOther(RelationAttributeModel a, RelationAttributeModel b)
        => (a.InverseProperty is not null
            && string.Equals(a.InverseProperty, b.NavigationName, StringComparison.Ordinal))
           || (b.InverseProperty is not null
               && string.Equals(b.InverseProperty, a.NavigationName, StringComparison.Ordinal));

    /// <summary>
    ///     Resolves one relationship from the declarations that describe it — at least one of the two
    ///     is present.
    /// </summary>
    /// <param name="parentTypeName">The simple name of the entity on the "one" side.</param>
    /// <param name="oneToMany">The parent's declaration, when it declares one.</param>
    /// <param name="manyToOne">The child's declaration, when it declares one.</param>
    public static ResolvedRelation Resolve(
        string parentTypeName,
        RelationAttributeModel? oneToMany,
        RelationAttributeModel? manyToOne)
    {
        // The child's navigation is the child's own member: it names it, or the parent names it for
        // it with Inverse, or it is called after the parent's type.
        var childNavigation = manyToOne?.NavigationName
                              ?? oneToMany?.InverseProperty
                              ?? parentTypeName;

        return new ResolvedRelation(
            childNavigation,
            RelationForeignKeyNaming.Name(manyToOne?.ForeignKeyProperty, childNavigation, parentTypeName),
            manyToOne?.IsRequired ?? true,
            oneToMany?.OnDelete ?? manyToOne?.OnDelete ?? "NoAction");
    }

    /// <summary>
    ///     Decides, for a one-to-one, which end is the principal and what the dependent's foreign key
    ///     is called.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both ends declare the same attribute, so only <c>IsPrincipal</c> can tell them apart.
    ///         Exactly one saying it is the answer. Neither and both are the ambiguous cases, and they
    ///         are what produced a one-to-one with two foreign keys and one with none — silently, since
    ///         each end reached its own conclusion.
    ///     </para>
    ///     <para>
    ///         When ambiguous a side is still chosen, by ordinal comparison of the type names, so the
    ///         emitted shape does not depend on the order the entities were walked in. It is the
    ///         diagnostic that stops the build, not a broken model.
    ///     </para>
    /// </remarks>
    /// <param name="owner">The entity whose declaration is being processed.</param>
    /// <param name="mine">Its <c>OneToOne</c> declaration.</param>
    /// <param name="other">The entity at the other end, when it is known.</param>
    /// <param name="theirs">The other end's declaration, when it declares one.</param>
    public static ResolvedOneToOne ResolveOneToOne(
        EntityMetadataModel owner,
        RelationAttributeModel mine,
        EntityMetadataModel? other,
        RelationAttributeModel? theirs)
    {
        var otherFullName = other?.FullTypeName ?? mine.TargetTypeFullName;

        // A lone declaration has to be the dependent: there is no second declaration to carry the
        // key, so a side that declares itself principal and nothing else leaves the relationship
        // without a foreign key at all.
        var claims = (mine.IsPrincipal, theirs?.IsPrincipal ?? false);
        var ambiguous = theirs is null
            ? mine.IsPrincipal
            : claims is (true, true) or (false, false);

        string principal;
        if (!ambiguous)
            principal = mine.IsPrincipal ? owner.FullTypeName : otherFullName;
        else
            principal = string.CompareOrdinal(owner.FullTypeName, otherFullName) <= 0
                ? owner.FullTypeName
                : otherFullName;

        var dependentDeclaration = principal == owner.FullTypeName ? theirs : mine;
        var principalDeclaration = principal == owner.FullTypeName ? mine : theirs;

        var dependentNavigation = RelationNavigationNaming.Reference(
            dependentDeclaration?.NavigationName ?? principalDeclaration?.InverseProperty,
            PrincipalTypeName(owner, mine, principal));

        return new ResolvedOneToOne(
            principal,
            RelationForeignKeyNaming.Name(
                dependentDeclaration?.ForeignKeyProperty,
                dependentNavigation,
                PrincipalTypeName(owner, mine, principal)),
            dependentDeclaration?.IsRequired ?? true,
            principalDeclaration?.OnDelete ?? dependentDeclaration?.OnDelete ?? "NoAction",
            ambiguous);
    }

    /// <summary>
    ///     Resolves the join of a many-to-many from both of its declarations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Neither end owns a symmetric relationship, so each option is taken from whichever end
    ///         names it — but both ends have to reach the same answer, and "mine, or else theirs" does
    ///         not: it hands each end a different one. The two declarations are put in a canonical
    ///         order first, by entity name, so the winner does not depend on who is asking.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>LeftKey</c> and <c>RightKey</c> are <b>not</b> symmetric: left is the declaring
    ///         end's key. Taken from the other end's declaration they mean the opposite, so they are
    ///         swapped back into the asking end's frame. The join table and the join entity are the
    ///         same seen from either end and need no such care.
    ///     </para>
    /// </remarks>
    /// <param name="ownerFullTypeName">The entity whose declaration is being processed.</param>
    /// <param name="mine">Its <c>ManyToMany</c> declaration.</param>
    /// <param name="targetFullTypeName">The entity at the other end.</param>
    /// <param name="theirs">The other end's declaration, when it declares one.</param>
    public static ResolvedManyToMany ResolveManyToMany(
        string ownerFullTypeName,
        RelationAttributeModel mine,
        string targetFullTypeName,
        RelationAttributeModel? theirs)
    {
        var ownerIsFirst = string.CompareOrdinal(ownerFullTypeName, targetFullTypeName) <= 0;
        var (first, second) = ownerIsFirst ? (mine, theirs) : (theirs, mine);

        var keySource = NamesAKey(first) ? first : NamesAKey(second) ? second : null;
        var keysAreMine = ReferenceEquals(keySource, mine);

        return new ResolvedManyToMany(
            first?.JoinTable ?? second?.JoinTable,
            first?.JoinEntityTypeName ?? second?.JoinEntityTypeName,
            keysAreMine ? keySource?.JoinLeftKey : keySource?.JoinRightKey,
            keysAreMine ? keySource?.JoinRightKey : keySource?.JoinLeftKey);
    }

    /// <summary>Whether this declaration names either end of the join.</summary>
    private static bool NamesAKey(RelationAttributeModel? relation)
        => !string.IsNullOrEmpty(relation?.JoinLeftKey) || !string.IsNullOrEmpty(relation?.JoinRightKey);

    /// <summary>The simple name of whichever end came out principal.</summary>
    private static string PrincipalTypeName(
        EntityMetadataModel owner, RelationAttributeModel mine, string principalFullName)
        => principalFullName == owner.FullTypeName ? owner.TypeName : mine.TargetTypeName;
}
