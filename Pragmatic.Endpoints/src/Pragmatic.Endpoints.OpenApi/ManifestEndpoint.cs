namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Endpoint metadata from the SG manifest.</summary>
public sealed class ManifestEndpoint
{
    public string? OperationId { get; set; }
    public string? HttpMethod { get; set; }
    public string? FullRoute { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public int SuccessStatusCode { get; set; }
    public bool IsVoid { get; set; }
    public ManifestResponse? Response { get; set; }
    public List<ManifestParam>? Parameters { get; set; }
    public ManifestBody? RequestBody { get; set; }
    public List<ManifestError>? Errors { get; set; }
    public ManifestAuth? Authorization { get; set; }

    // Extra metadata for richer OpenAPI generation.
    public bool IsDeprecated { get; set; }
    public string? RateLimitPolicy { get; set; }
    public int? CacheDurationSeconds { get; set; }
    public List<string>? Tags { get; set; }
    public ManifestFileUpload? FileUpload { get; set; }
    public long? MaxBodySizeBytes { get; set; }
    public List<ManifestExample>? RequestExamples { get; set; }
    public List<ManifestResponseExample>? ResponseExamples { get; set; }
    public string? IdempotencyHeader { get; set; }
    public ManifestMcp? Mcp { get; set; }
}
