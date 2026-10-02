namespace Pragmatic.Endpoints.OpenApi;

/// <summary>[McpTool] exposure metadata from the SG manifest.</summary>
public sealed class ManifestMcp
{
    public bool Enabled { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}
