using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     Model for a class marked with [PragmaticUser].
///     Drives profile adapter and user resolver generation.
/// </summary>
internal sealed record UserEntityModel : GeneratorModel
{
    /// <summary>
    ///     The entity's key type, as it appears in <c>IReadRepository&lt;TEntity&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     The resolver reads it: it asks for the repository rather than a <c>DbContext</c>, and the repository is generic in
    ///     the key.
    /// </remarks>
    public required string KeyTypeName { get; init; }

    /// <summary>The claim type to match (default: "sub").</summary>
    public required string MatchClaim { get; init; }

    /// <summary>
    ///     The entity property path to match against the claim value.
    ///     Can be a direct property (e.g., "ExternalIdentityKey") or a navigation path
    ///     (e.g., "Identity.ExternalIdentityKey" when Identity is an owned entity).
    /// </summary>
    public required string MatchProperty { get; init; }

    /// <summary>
    ///     Whether the match property is on a navigation (e.g., "Identity.ExternalIdentityKey").
    ///     When true, the resolver uses null-conditional navigation in the LINQ query.
    /// </summary>
    public bool MatchPropertyIsNavigation { get; init; }

    /// <summary>Properties marked with [ProfileProperty].</summary>
    public EquatableArray<ProfilePropertyModel> ProfileProperties { get; init; } = EquatableArray<ProfilePropertyModel>.Empty;

    /// <summary>
    ///     Whether EF Core persists the user here (<c>Microsoft.EntityFrameworkCore</c> referenced): the generator writes the
    ///     <c>IUserLookup&lt;TUser&gt;</c> over the repository's <c>Query()</c>.
    /// </summary>
    public bool LooksUpThroughEfCore { get; init; }

    /// <summary>
    ///     Whether the Sql engine persists the user here (its bridge referenced): its generated read repository is the user's
    ///     <c>IUserLookup&lt;TUser&gt;</c>, registered by the boundary's <c>Add{Boundary}Persistence</c> (#593).
    /// </summary>
    public bool LooksUpThroughSql { get; init; }

    /// <summary>
    ///     Whether the documents engine persists the user here (its bridge referenced): its generated read repository is the user's
    ///     <c>IUserLookup&lt;TUser&gt;</c>, registered by the boundary's <c>Add{Boundary}Persistence</c> (#594).
    /// </summary>
    public bool LooksUpThroughDocuments { get; init; }

    /// <summary>
    ///     Whether an <c>IUserLookup&lt;TUser&gt;</c> exists for the user: what the resolver reads through, so what decides whether the resolver, its
    ///     registration, the culture provider and the current-user bindings are generated.
    /// </summary>
    public bool HasUserLookup => LooksUpThroughEfCore || LooksUpThroughSql || LooksUpThroughDocuments;

    /// <summary>
    ///     What a query may bind with <c>[FromCurrentUser(member)]</c>: the entity's readable members,
    ///     including the <c>Id</c> an <c>[Entity]</c> has before the generator writes it.
    /// </summary>
    public EquatableArray<UserMemberModel> Members { get; init; } = EquatableArray<UserMemberModel>.Empty;
}

/// <summary>
///     Model for a single [ProfileProperty]-marked property.
/// </summary>
internal sealed record ProfilePropertyModel
{
    /// <summary>The property name.</summary>
    public required string Name { get; init; }

    /// <summary>The property type name (e.g., "string", "string?").</summary>
    public required string TypeName { get; init; }

    /// <summary>Whether the property type is nullable.</summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether this is a well-known profile property (PreferredCulture, TimeZone)
    ///     that maps directly to IUserProfile.
    /// </summary>
    public bool IsWellKnown { get; init; }
}
