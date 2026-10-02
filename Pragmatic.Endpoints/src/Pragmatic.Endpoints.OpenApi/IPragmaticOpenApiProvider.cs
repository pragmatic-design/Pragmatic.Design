namespace Pragmatic.Endpoints.OpenApi;

// There is no per-host provider interface with a `static abstract string Json`: a host's own document
// is HostOpenApiDocument, a service in the host's container, which a generated composition can
// register and a mount can resolve. A static-abstract interface could do neither.

/// <summary>
///     Static registry for the SG-generated OpenAPI JSON — the <b>fallback</b> for a host that
///     registers no <see cref="HostOpenApiDocument" /> of its own.
/// </summary>
/// <remarks>
///     ⚠️ One field for the whole process, written by a <c>[ModuleInitializer]</c> the generator emits
///     per host: module initializers run at assembly load, in load order, and last writer wins, so two
///     hosts in one process leave this holding whichever loaded second. It is kept because
///     every single-host deployment has always read it and must keep behaving identically; what a host
///     registers in its container wins over it.
/// </remarks>
public static class PragmaticOpenApiRegistry
{
    private static string? _json;

    /// <summary>
    ///     Registers the compile-time generated OpenAPI JSON.
    ///     Called by SG-generated code.
    /// </summary>
    /// <param name="json">The OpenAPI JSON document.</param>
    /// <param name="requiresAuthentication">
    ///     Whether at least one operation is not <c>[AllowAnonymous]</c>. The document itself does
    ///     not say: which operations need authentication is settled at compile time, but what the
    ///     requirement looks like on the wire is registered by whoever set the authentication up, so
    ///     the mount composes the two. Without this flag the mount cannot tell an application that
    ///     needs no authentication from one whose authentication described itself to nobody.
    /// </param>
    public static void Register(string json, bool requiresAuthentication = false)
    {
        _json = json;
        RequiresAuthentication = requiresAuthentication;
    }

    /// <summary>
    ///     Gets the registered OpenAPI JSON, or null if not registered.
    /// </summary>
    public static string? Json => _json;

    /// <summary>Whether the registered document has an operation that requires authentication.</summary>
    public static bool RequiresAuthentication { get; private set; }
}
