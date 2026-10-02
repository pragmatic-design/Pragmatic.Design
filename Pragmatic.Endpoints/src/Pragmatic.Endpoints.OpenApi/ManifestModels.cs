namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Deserialized manifest document.</summary>
public sealed class ManifestDocument
{
    public string? Assembly { get; set; }
    public string? Version { get; set; }
    public List<ManifestEndpoint>? Endpoints { get; set; }
    public List<ManifestType>? Types { get; set; }
    public List<ManifestPermission>? Permissions { get; set; }

    /// <summary>True for the host-aggregated form, whose payload lives in <see cref="Modules" />.</summary>
    public bool Aggregated { get; set; }

    /// <summary>Per-module manifests embedded in the host-aggregated form.</summary>
    public List<ManifestDocument>? Modules { get; set; }
}
