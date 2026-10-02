using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0735</c>: the body of a <c>[Projectable]</c> or <c>[ComputedFilter]</c> member, as the
///     query receives it.
/// </summary>
/// <remarks>
///     Beside <see cref="ComputedFilterDiagnostics" />: the projection feature reports it, for both kinds
///     of member, since both bodies go through the same rewrite.
/// </remarks>
internal static class ComputedBodyDiagnostics
{
    /// <summary>
    ///     A specification passed to a query from a computed body takes a value from the row.
    /// </summary>
    /// <remarks>
    ///     The query receives the specification's expression, and the query provider asks for it before
    ///     translating — when no row exists yet. EF Core throws <c>InvalidCastException</c> at the first
    ///     query that reads the member (measured); the build is the earlier place to say so.
    /// </remarks>
    public static readonly DiagnosticDescriptor SpecificationReadsTheRow = DiagnosticFactory.Error(
        "PRAG0735",
        "A specification in a computed body takes a value from the row",
        "'{0}.{1}' passes '{2}' to a query, and it takes a value from the row: the query builds the "
        + "specification before it reads any row",
        "Inside a [Projectable] or [ComputedFilter] body a specification is evaluated before the query is "
        + "translated, and its expression is inlined. Its arguments can be constants, static members or a "
        + "filter method's parameters. A member of the entity, or a lambda parameter of the body, exists "
        + "only inside the query: write that condition as a lambda instead.");
}
