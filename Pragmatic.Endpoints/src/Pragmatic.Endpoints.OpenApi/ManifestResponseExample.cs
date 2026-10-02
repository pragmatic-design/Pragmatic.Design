namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Response body example (per status code) from the SG manifest.</summary>
public sealed class ManifestResponseExample
{
    public int StatusCode { get; set; }
    public string? Name { get; set; }
    public string? Json { get; set; }
}
