using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0732</c>: the grouping a <c>[QueryView]</c> declares.
/// </summary>
/// <remarks>
///     Beside <see cref="QueryPipelineDiagnostics" /> rather than in it, like the other families of the
///     range that live where the code that emits them does: the view transform is the one that resolves
///     the keys.
/// </remarks>
internal static class QueryViewDiagnostics
{
    /// <summary>
    ///     A <c>[GroupBy]</c> key, or the <c>Via</c> that reaches it, names no member of the entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An error, because there is nothing to fall back to: a view without the key groups by fewer
    ///         columns than declared and adds up across the missing one — a report per team that sums
    ///         every team. That is what the view did while the key was skipped.
    ///     </para>
    ///     <para>
    ///         A member a generator writes — a relation's foreign key or navigation, a trait's column — is
    ///         a member: it is looked up among the predicted ones before this fires.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor GroupKeyDoesNotResolve = DiagnosticFactory.Error(
        "PRAG0732",
        "A group key names no member of the entity",
        "View '{0}' groups by {1}, which '{2}' does not have: without it the view would add up across it",
        "Name a property of the entity — declared, or one its relations and traits generate — and reach "
        + "another entity with Via = \"Navigation\".");

    /// <summary>
    ///     <c>IncludeEmpty</c> on a view grouped by more than one key (#766).
    /// </summary>
    /// <remarks>
    ///     Two keys of known values multiply (statuses × countries), and one known with one not has no set to fill from: the opt-in answers a
    ///     single key, decided with the owner on the issue.
    /// </remarks>
    public static readonly DiagnosticDescriptor EmptyGroupsOnACompositeKey = DiagnosticFactory.Error(
        "PRAG0748",
        "IncludeEmpty needs a single group key",
        "View '{0}' asks for its empty groups (IncludeEmpty = true) but groups by {1}: the values of more than one key multiply, or are not known",
        "Group by one enum key, or drop IncludeEmpty and join the answer to the values yourself.");

    /// <summary>
    ///     <c>IncludeEmpty</c> on a key whose values the generator does not know, or cannot set (#766).
    /// </summary>
    /// <remarks>
    ///     An enum's members are known at compile time and written into the view; a lookup's are rows read at startup, and another type's are
    ///     not known at all. The key must be a property of the view too, since the empty row carries its value.
    /// </remarks>
    public static readonly DiagnosticDescriptor EmptyGroupsOnAKeyNotAProjectedEnum = DiagnosticFactory.Error(
        "PRAG0749",
        "IncludeEmpty needs an enum key the view carries",
        "View '{0}' asks for its empty groups (IncludeEmpty = true) on the key {1}: only an enum key, projected onto a property of the view, has values to fill from",
        "Group by an enum, with a view property of the same name, or drop IncludeEmpty.");

    /// <summary>
    ///     A member an empty group has no value for, not nullable, on a view with <c>IncludeEmpty</c> (#766).
    /// </summary>
    /// <remarks>
    ///     <c>[Min]</c>, <c>[Max]</c>, <c>[Avg]</c> and a <c>[From]</c> other than the key have nothing to read when no row is in the group. A
    ///     non-nullable property would answer its default, a 0 a caller cannot tell from a real one; a nullable one answers null.
    /// </remarks>
    public static readonly DiagnosticDescriptor EmptyGroupMemberHasNoValue = DiagnosticFactory.Error(
        "PRAG0750",
        "An empty group has no value for this member",
        "View '{0}' asks for its empty groups (IncludeEmpty = true), and {1} has no value in a group of no rows: a non-nullable property would answer a default nobody can tell from a real value",
        "Make the property nullable, so an empty group answers null, or drop IncludeEmpty.");
}
