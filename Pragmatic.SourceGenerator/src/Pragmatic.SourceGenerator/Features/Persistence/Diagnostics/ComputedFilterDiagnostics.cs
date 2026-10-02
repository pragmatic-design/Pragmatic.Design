using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0733</c>: a <c>[ComputedFilter]</c> the generator cannot write.
/// </summary>
/// <remarks>
///     Beside <see cref="QueryPipelineDiagnostics" /> rather than in it, like the other families of the
///     range that live where the code that emits them does: the projection feature reports it.
/// </remarks>
internal static class ComputedFilterDiagnostics
{
    /// <summary>
    ///     A <c>[ComputedFilter]</c> member the generator cannot turn into a specification. The reason is
    ///     the last argument.
    /// </summary>
    /// <remarks>
    ///     An error, not a silent skip: without it a property of another type than <c>bool</c> would be
    ///     dropped with nothing said — the attribute compiles, and no <c>Where{Name}()</c> exists for
    ///     anyone to call.
    /// </remarks>
    public static readonly DiagnosticDescriptor ComputedFilterCannotBeGenerated = DiagnosticFactory.Error(
        "PRAG0733",
        "A [ComputedFilter] the generator cannot write",
        "'{0}.{1}' is [ComputedFilter] and cannot be generated: {2}",
        "A computed filter is a bool property or an instance method returning bool, with an expression "
        + "body. A method's parameters are the values the rule takes from outside the row, passed as they "
        + "are: no ref, out, in or params.");
}
