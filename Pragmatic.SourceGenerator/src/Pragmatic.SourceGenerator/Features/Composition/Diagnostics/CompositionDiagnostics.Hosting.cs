using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Composition.Diagnostics;

/// <summary>What a host hosts, checked against what its modules need.</summary>
internal static partial class CompositionDiagnostics
{
    /// <summary>
    ///     PRAG1603: a hosted module depends on a module the host neither hosts nor declares remote.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generated host registers exactly what it declares and does not follow
    ///         <c>[IncludeModule&lt;T&gt;]</c>, so such a host compiled and failed when the dependency was
    ///         first resolved. <c>PRAG1601</c> is a different question — whether the dependency names a
    ///         module at all.
    ///     </para>
    ///     <para>
    ///         No runtime validator can catch this. The host's generator sees every hosted module and every
    ///         declaration, so the check lives here and fails the build.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor HostedModuleDependencyNotHosted = new(
        "PRAG1603",
        "A hosted module depends on a module the host does not host",
        "Host '{0}' hosts module '{1}', which depends on module '{2}', and neither includes nor declares it remote. Host it here with [Include<T>] on its module type, or declare [RemoteBoundary<T>] if it runs elsewhere.",
        Category,
        DiagnosticSeverity.Error,
        true);
}
