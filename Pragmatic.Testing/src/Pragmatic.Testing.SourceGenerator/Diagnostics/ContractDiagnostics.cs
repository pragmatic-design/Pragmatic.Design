using Microsoft.CodeAnalysis;

namespace Pragmatic.Testing.SourceGenerator.Diagnostics;

/// <summary>
///     Diagnostics of the contract-test generator. Its range is <c>PRAG2363</c> onward: <c>PRAG2350-2362</c>
///     belong to the mock generator.
/// </summary>
internal static class ContractDiagnostics
{
    private const string Category = "Pragmatic.Testing";

    /// <summary>
    ///     PRAG2363: half of a state-transition contract has no walk to stand on.
    /// </summary>
    /// <remarks>
    ///     The half that does not start from the initial state needs a state the declared transitions reach
    ///     and the target may (or may not) be entered from. When no walk exists there is no test to emit, and
    ///     the generator reports it rather than emitting a skipped placeholder, which a suite counts and
    ///     nobody reads.
    /// </remarks>
    public static readonly DiagnosticDescriptor TransitionHalfUnreachable = new(
        "PRAG2363",
        "A state-transition contract has no walk to its source state",
        "No declared transition of '{0}' reaches a state from which '{1}' is {2}: that half of its contract is not generated",
        Category,
        DiagnosticSeverity.Info,
        true,
        "Expose the transition that leads there, or write the contract by hand in the partial test class.");
}
