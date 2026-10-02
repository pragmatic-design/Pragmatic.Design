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
}
