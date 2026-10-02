using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Diagnostics;

/// <summary>
///     What the generator says about <b>itself</b>: not a rule of Actions, of Endpoints or of
///     Persistence, but a fact about generation — the <c>PRAG9000-9099</c> range beside
///     <c>SafeSourceOutput</c>'s <c>PRAG9000</c>.
/// </summary>
internal static class GenerationDiagnostics
{
    private const string Category = "Pragmatic.SourceGenerator";

    /// <summary>
    ///     A type an operation names does not resolve, so nothing was generated for that operation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Roslyn hands the generator an <b>error symbol</b>. It is still an
    ///         <c>INamedTypeSymbol</c>, so every check passes, and
    ///         <c>ToDisplayString(FullyQualifiedFormat)</c> on it is the <b>bare name with no
    ///         namespace</b> — which written back out becomes <c>global::CallerDto</c>, a name that
    ///         cannot exist. Measured: one missing <c>using</c> in one hand-written file produced
    ///         <b>a page</b> of <c>CS0246</c>/<c>CS0400</c> across the endpoint contract, the three
    ///         boundary facades, the invoker and the registration.
    ///     </para>
    ///     <para>
    ///         ⚠️ The cost is not the noise, it is the <b>wrong diagnosis</b>: a <c>CS0400</c> inside a
    ///         generated file sends the reader looking for a generator bug, and such reports land, in good
    ///         faith, against templates that are already correct.
    ///     </para>
    ///     <para>
    ///         Warning and not Error: the compiler is already reporting the real error, in the file that
    ///         has it. This one says which operation it stopped, so the reader goes there instead of into
    ///         a generated file.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor UnresolvedTypeStoppedGeneration = new(
        "PRAG9001",
        "A type the operation names does not resolve",
        "'{0}' names '{1}', which does not resolve, so nothing was generated for it. Fix that name — a "
        + "missing using in its own file, most often — and the generated code comes back with it.",
        Category,
        DiagnosticSeverity.Warning,
        true);
}
