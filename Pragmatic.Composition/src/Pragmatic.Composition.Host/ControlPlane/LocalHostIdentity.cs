using System.Text.Json;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Metadata;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Default host identity — reads the logical name from SG topology metadata
///     and generates a unique instance ID on startup.
/// </summary>
public sealed class LocalHostIdentity : IHostIdentity
{
    /// <summary>
    ///     Creates a host identity with a fresh unique instance ID, the current UTC start time, and the
    ///     logical host name this host was given — or, when it was given none, the one the SG topology
    ///     metadata answers with.
    /// </summary>
    /// <param name="hostType">The role this host plays in the topology. Defaults to <see cref="HostType.Tenant" />.</param>
    /// <param name="hostName">
    ///     This host's own logical name, which its generated composition passes as a compile-time
    ///     constant.
    /// </param>
    /// <remarks>
    ///     ⚠️ <b>Why the name is a parameter.</b> <see cref="AssemblyMetadataRegistry" />, read with
    ///     <c>FindByCategory</c>, answers with the <b>first</b> provider that carries a topology. The
    ///     generator emits one provider per host from a <c>[ModuleInitializer]</c>, so with two hosts in
    ///     one process the registry alone would give both the name of whichever assembly the runtime
    ///     loaded first, and file the second host's control-plane identity under its neighbour's. A
    ///     host's name is a fact about that host, so the host says it; the registry is the fallback for
    ///     a host that does not.
    /// </remarks>
    public LocalHostIdentity(HostType hostType = HostType.Tenant, string? hostName = null)
    {
        HostId = Guid.CreateVersion7().ToString("N");
        StartedAt = DateTimeOffset.UtcNow;
        HostType = hostType;
        HostName = string.IsNullOrWhiteSpace(hostName) ? ResolveHostName() : hostName!;
    }

    /// <inheritdoc />
    public string HostId { get; }

    /// <inheritdoc />
    public string HostName { get; }

    /// <inheritdoc />
    public HostType HostType { get; }

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    ///     Resolves the logical host name from the SG-populated <see cref="AssemblyMetadataRegistry"/>,
    ///     falling back to <see cref="Environment.MachineName"/> when the host carries no topology
    ///     metadata. Identical under JIT and AOT.
    /// </summary>
    private static string ResolveHostName()
    {
        // The generator emits a module initializer that fills this registry, so a host built by
        // Pragmatic has the entry before any of this runs. A reflective read of the same attribute
        // behind it would add nothing: it could only succeed where the registry already had, and the
        // machine name is the honest answer when neither is present.
        var entry = AssemblyMetadataRegistry.FindByCategory(MetadataCategory.HostTopology);

        return entry.HasValue ? ParseHostNameFromJson(entry.Value.JsonData) : Environment.MachineName;
    }

    private static string ParseHostNameFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("host", out var hostProp)
                ? hostProp.GetString() ?? Environment.MachineName
                : Environment.MachineName;
        }
        catch (JsonException)
        {
            return Environment.MachineName;
        }
    }
}
