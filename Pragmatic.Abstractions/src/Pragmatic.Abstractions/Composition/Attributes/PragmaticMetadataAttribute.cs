using Pragmatic.Composition.Metadata;

namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Assembly-level attribute that stores metadata for auto-discovery by HOST generators.
///     Each source generator emits this attribute with category-specific JSON data.
/// </summary>
/// <remarks>
///     <para>
///         This attribute enables zero-reflection auto-discovery of services, mappings, actions,
///         and other Pragmatic components across assembly boundaries.
///     </para>
///     <para>
///         The HOST generator reads these attributes from referenced assemblies and generates
///         aggregated registration code.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PragmaticMetadataAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PragmaticMetadataAttribute" /> class.
    /// </summary>
    /// <param name="category">The metadata category.</param>
    /// <param name="schemaVersion">The schema version (e.g., "1.0.0").</param>
    /// <param name="jsonData">The JSON metadata payload.</param>
    public PragmaticMetadataAttribute(MetadataCategory category, string schemaVersion, string jsonData)
    {
        Category = category;
        SchemaVersion = schemaVersion;
        JsonData = jsonData;
    }

    /// <summary>
    ///     Gets the metadata category (DI, Mapping, Actions, etc.).
    /// </summary>
    public MetadataCategory Category { get; }

    /// <summary>
    ///     Gets the schema version for backward compatibility.
    ///     Uses semantic versioning (major.minor.patch).
    /// </summary>
    public string SchemaVersion { get; }

    /// <summary>
    ///     Gets the JSON data containing category-specific metadata.
    /// </summary>
    public string JsonData { get; }
}
