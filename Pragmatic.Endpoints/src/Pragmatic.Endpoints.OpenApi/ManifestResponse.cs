namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Response type metadata.</summary>
public sealed class ManifestResponse
{
    public string? Type { get; set; }
    public bool IsPaged { get; set; }
    public bool IsStream { get; set; }
}
