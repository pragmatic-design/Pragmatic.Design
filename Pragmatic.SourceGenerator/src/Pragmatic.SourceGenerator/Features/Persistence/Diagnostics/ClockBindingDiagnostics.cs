using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0734</c>: a property of a query, an action or a mutation bound from the clock with <c>[FromClock]</c>.
/// </summary>
/// <remarks>
///     Beside <see cref="QueryPipelineDiagnostics" /> rather than in it, like the other families of the
///     range that live where the code that emits them does.
/// </remarks>
internal static class ClockBindingDiagnostics
{
    /// <summary>
    ///     A <c>[FromClock]</c> binding the invoker cannot write: the reason is the last argument.
    /// </summary>
    public static readonly DiagnosticDescriptor ClockBindingCannotBeGenerated = DiagnosticFactory.Error(
        "PRAG0734",
        "The [FromClock] binding cannot be generated",
        "'{0}.{1}' cannot be bound from the clock: {2}",
        "[FromClock] fills a DateOnly with IClock.UtcToday and a DateTimeOffset with IClock.UtcNow. The "
        + "property is declared '{ get; private set; }': the generated nested invoker reaches that setter, "
        + "and nothing else does.");
}
