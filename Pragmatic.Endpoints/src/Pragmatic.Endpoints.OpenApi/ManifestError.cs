namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Error response metadata.</summary>
public sealed class ManifestError
{
    public string? Type { get; set; }
    public string? Code { get; set; }
    public int StatusCode { get; set; }
    public List<ManifestErrorExt>? Extensions { get; set; }
}
