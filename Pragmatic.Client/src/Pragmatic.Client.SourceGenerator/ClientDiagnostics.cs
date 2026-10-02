using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator;

/// <summary>
///     Diagnostics for the typed-client generator (PRAG2300-2349, see docs/diagnostics.md).
///     <para>
///         Before these existed the generator was silent: a malformed manifest produced a <c>.g.cs</c> holding a
///         single C# comment, the build stayed green, and the failure surfaced to the consumer as CS0246 on
///         types that were never generated.
///     </para>
/// </summary>
internal static class ClientDiagnostics
{
    /// <summary>PRAG2300: the manifest could not be read, so no client was generated from it.</summary>
    public static readonly DiagnosticDescriptor ManifestUnreadable = DiagnosticFactory.Error(
        "PRAG2300",
        "API manifest could not be read",
        "The API manifest '{0}' could not be read ({1}). No typed client was generated from it.",
        "The manifest is either not valid JSON or does not match the expected schema. Rebuild the project that " +
        "produces it, or check the manifest file passed as AdditionalFiles.");

    /// <summary>PRAG2301: a response/property type is absent from the manifest type table, so it degrades to object.</summary>
    public static readonly DiagnosticDescriptor UnresolvedType = DiagnosticFactory.Warning(
        "PRAG2301",
        "Client type degraded to object",
        "Type '{0}' is not described in the manifest, so the generated client exposes it as 'object'.",
        "The generated client can only name types the manifest describes. Everything else becomes 'object', " +
        "which compiles but leaves the caller without a typed payload. Expose the type as a DTO on the " +
        "boundary so it is emitted into the manifest.");

    /// <summary>PRAG2303: the endpoint returns a result, but the manifest does not say of what type.</summary>
    public static readonly DiagnosticDescriptor ResponseTypeMissing = DiagnosticFactory.Warning(
        "PRAG2303",
        "Endpoint response type missing from the manifest",
        "Endpoint '{0}' is not void but the manifest carries no response type, so the generated client returns 'object'.",
        "Distinct from PRAG2301: there the type is named but not described, here nothing is stated at all. " +
        "The manifest producer could not determine what the endpoint returns — typically an endpoint whose " +
        "handler is itself generated. Fix it on the manifest side; the client has nothing to work with.");

    /// <summary>PRAG2302: the boundary filter excluded every endpoint, so nothing was generated.</summary>
    public static readonly DiagnosticDescriptor BoundaryFilterMatchedNothing = DiagnosticFactory.Warning(
        "PRAG2302",
        "Client boundary filter matched no endpoint",
        "PragmaticClientBoundaries '{0}' matched no endpoint in manifest '{1}'. No client was generated.",
        "The filter matches the prefix of each endpoint's operationId (for example 'Booking' for " +
        "'Booking.CreateGuest'). A typo silently produces no output.");

    /// <summary>PRAG2304: two manifests describe the same type name differently; only the first shape is emitted.</summary>
    public static readonly DiagnosticDescriptor SharedTypeShapeConflict = DiagnosticFactory.Warning(
        "PRAG2304",
        "Shared client type described differently by two manifests",
        "Type '{0}' is described differently by two manifests. The first description was kept and the " +
        "second discarded, so one boundary's client may be typed against the wrong shape.",
        "A type reachable from several boundaries is emitted once, since a per-boundary copy would " +
        "collide on the same file name. That is safe while both manifests agree. When they disagree, " +
        "the same simple name denotes two different types — give one of them a distinct name, or expose " +
        "a dedicated DTO per boundary.");
}
