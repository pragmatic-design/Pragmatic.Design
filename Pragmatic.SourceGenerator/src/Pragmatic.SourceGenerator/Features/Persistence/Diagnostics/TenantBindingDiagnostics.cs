using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0747</c>: a property of a query, an action or a mutation bound to the request's tenant with
///     <c>[FromTenant]</c>.
/// </summary>
/// <remarks>Beside <see cref="ClockBindingDiagnostics" />, the binding it is the sibling of.</remarks>
internal static class TenantBindingDiagnostics
{
    /// <summary>
    ///     A <c>[FromTenant]</c> binding the invoker cannot write: the reason is the last argument.
    /// </summary>
    public static readonly DiagnosticDescriptor TenantBindingCannotBeGenerated = DiagnosticFactory.Error(
        "PRAG0747",
        "The [FromTenant] binding cannot be generated",
        "'{0}.{1}' cannot be bound to the request's tenant: {2}",
        "[FromTenant] fills a string with ITenantContext.TenantId. The property is declared "
        + "'{ get; private set; }': the generated nested invoker reaches that setter, and nothing else does.");
}
