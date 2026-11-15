using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     What a <c>[TenantSource]</c> cannot name (PRAG0642).
/// </summary>
internal static partial class PersistenceDiagnostics
{
    /// <summary>PRAG0642: <c>[TenantSource]</c> names a member that does not exist or has the wrong type.</summary>
    /// <remarks>
    ///     The store over the entity reads those members on every request. Refused here, with no store generated, rather than leaving the
    ///     host on the empty in-memory store, where every tenant is unknown.
    /// </remarks>
    public static readonly DiagnosticDescriptor TenantSourceMemberInvalid = DiagnosticFactory.Error(
        "PRAG0642",
        "[TenantSource] names a member the store cannot read",
        "[TenantSource] on '{0}': {1} — Id and Name name string properties, State a TenantState one, CreatedAt (optional) a DateTimeOffset one",
        "The tenant store generated over a [TenantSource] entity reads the members it names, so each must exist with its type.");
}
