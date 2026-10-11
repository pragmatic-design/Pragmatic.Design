namespace Pragmatic.Endpoints.OpenApi;

/// <summary>Body property metadata.</summary>
public sealed class ManifestProperty
{
    public string? Name { get; set; }

    /// <summary>The name on the wire when <c>[JsonPropertyName]</c> renames the property; null otherwise.</summary>
    public string? WireName { get; set; }

    public string? Type { get; set; }
    public bool IsRequired { get; set; }
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
}
