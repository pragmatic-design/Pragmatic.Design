namespace Pragmatic.Endpoints.OpenApi;

/// <summary>File-upload constraints from the SG manifest.</summary>
public sealed class ManifestFileUpload
{
    public long? MaxFileSizeBytes { get; set; }
    public List<string>? AllowedContentTypes { get; set; }
}
