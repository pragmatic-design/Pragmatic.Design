namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Which end of a one-to-one carries the foreign key, and what that key is called.
/// </summary>
/// <remarks>
///     <para>
///         Both ends of a one-to-one declare the same attribute, so the roles cannot come from the
///         attribute's kind the way they do for <c>OneToMany</c>/<c>ManyToOne</c>: they come from
///         <c>IsPrincipal</c>. Deciding once, for the pair, is what keeps the two ends from each
///         reaching its own answer — which would produce a relationship with two foreign keys when
///         nobody claims to be principal, and one with none when both do.
///     </para>
///     <para>
///         <see cref="IsAmbiguous" /> marks a pair where <c>IsPrincipal</c> did not decide. Generation
///         still picks a side, deterministically, so the emitted shape does not depend on the order
///         the entities were walked in; what stops the build is the diagnostic.
///     </para>
/// </remarks>
internal sealed record ResolvedOneToOne(
    string PrincipalFullTypeName,
    string ForeignKeyName,
    bool IsRequired,
    string OnDelete,
    bool IsAmbiguous);
