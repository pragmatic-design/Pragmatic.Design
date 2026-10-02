using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Descriptor for <c>PRAG0441</c>, the boundary-facade injection warning.
/// </summary>
internal static class BoundaryActionsInjectionDescriptors
{
    private const string Category = "Pragmatic.Actions";

    /// <summary>
    ///     PRAG0441: a generated boundary-actions interface injected where the caller is not trusted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generated <c>{Boundary}LocalActions</c> wraps every method in
    ///         <c>ICallContext.EnterInternalCall()</c>, and authorization filters skip internal calls.
    ///         That is deliberate: an action composing its own boundary's operations has already had
    ///         the caller's permission checked at the entry point, and asking again would demand a
    ///         second grant to do half of one thing.
    ///     </para>
    ///     <para>
    ///         What is not deliberate is how easy it is to reach for on the request edge. The facade
    ///         is public, registered, and one dependency instead of one per operation — the natural
    ///         choice in a hand-written endpoint, where it silently turns authorization off. Measured
    ///         on a consumer app: the same mutation, in one scope with no user, is refused through
    ///         <c>IMutationInvoker</c> and succeeds through the facade.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor Prag0441 = new(
        "PRAG0441",
        "Boundary actions facade injected outside a trusted caller",
        "'{0}' enters an internal call, and authorization filters skip internal calls. Injecting it "
        + "into '{1}' runs its operations without checking the caller's permission. Inject "
        + "IMutationInvoker<,> or IDomainActionInvoker<,> for the one operation needed, or move the "
        + "call into an action, handler or job, where the permission is checked at the entry point.",
        Category,
        DiagnosticSeverity.Warning,
        true,
        "A boundary facade is the trusted in-process path. Reaching for it on the request edge is a "
        + "permission bypass that leaves no trace, because a permission that is never asked for is "
        + "not a permission that is denied.");
}
