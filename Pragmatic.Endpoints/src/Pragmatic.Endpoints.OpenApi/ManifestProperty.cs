namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Body property metadata.</summary>
public sealed class ManifestProperty
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool IsRequired { get; set; }
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
}
