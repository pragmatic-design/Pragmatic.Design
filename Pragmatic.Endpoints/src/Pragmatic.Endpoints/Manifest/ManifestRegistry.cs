using System.Collections.Immutable;

namespace Pragmatic.Endpoints.Manifest;

/// <summary>
///     Zero-reflection registry for SG-generated manifest JSON strings.
///     The SG generates calls to <see cref="Register" /> in module initializers.
///     Thread-safe: uses <see cref="ImmutableInterlocked"/> for lock-free concurrent registration.
/// </summary>
/// <remarks>
///     It lives here, not beside the reader in <c>Pragmatic.Endpoints.OpenApi</c>, because the
///     registration is generated into every assembly with endpoints, and the generated code may only name
///     what that assembly references. A library of endpoints references this package; it need not
///     reference the one an application publishes a document with.
/// </remarks>
public static class ManifestRegistry
{
    private static ImmutableList<string> s_manifests = ImmutableList<string>.Empty;

    /// <summary>Registers a manifest JSON string. Called by SG-generated code.</summary>
    public static void Register(string json)
        => ImmutableInterlocked.Update(ref s_manifests, static (list, item) => list.Add(item), json);

    /// <summary>Gets all registered manifest JSON strings.</summary>
    public static IReadOnlyList<string> GetAll() => s_manifests;
}
