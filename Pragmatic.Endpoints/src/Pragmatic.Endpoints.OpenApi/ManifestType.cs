namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Type metadata for schema generation.</summary>
public sealed class ManifestType
{
    public string? Type { get; set; }
    public string? SimpleName { get; set; }
    public string? Kind { get; set; }
    public string? ErrorCode { get; set; }
    public int? ErrorStatusCode { get; set; }
}
