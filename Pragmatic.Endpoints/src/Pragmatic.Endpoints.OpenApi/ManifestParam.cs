namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Parameter metadata (route, query, header).</summary>
public sealed class ManifestParam
{
    public string? Name { get; set; }
    public string? In { get; set; }
    public string? Type { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
}
