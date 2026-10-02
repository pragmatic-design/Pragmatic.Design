using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     The validation keywords of a property schema, copied from the manifest.
/// </summary>
internal static partial class OpenApiJsonGenerator
{
    /// <summary>
    ///     Writes every constraint the manifest carries for the property onto its schema, after the
    ///     type has been rendered.
    /// </summary>
    /// <remarks>
    ///     Only <c>maxLength</c> was copied for years, so the document described a server more
    ///     permissive than the validator behind it. The keywords arrive under their own names — the
    ///     manifest writes them in JSON Schema vocabulary — and are placed on the same object as the
    ///     type, where a reader applies them. A collection's bounds are <c>minItems</c>/<c>maxItems</c>,
    ///     decided upstream from the property's type; nothing here has to know what the property is.
    /// </remarks>
    private static void RenderConstraints(MetadataJsonBuilder b, PropDto p)
    {
        if (p.MinLength.HasValue) b.Property("minLength").Value(p.MinLength.Value);
        if (p.MaxLength.HasValue) b.Property("maxLength").Value(p.MaxLength.Value);
        if (p.Pattern is not null) b.Property("pattern").Value(p.Pattern);
        if (p.Format is not null) b.Property("format").Value(p.Format);
        if (p.Minimum.HasValue) b.Property("minimum").Value(p.Minimum.Value);
        if (p.Maximum.HasValue) b.Property("maximum").Value(p.Maximum.Value);
        if (p.ExclusiveMinimum.HasValue) b.Property("exclusiveMinimum").Value(p.ExclusiveMinimum.Value);
        if (p.ExclusiveMaximum.HasValue) b.Property("exclusiveMaximum").Value(p.ExclusiveMaximum.Value);
        if (p.MinItems.HasValue) b.Property("minItems").Value(p.MinItems.Value);
        if (p.MaxItems.HasValue) b.Property("maxItems").Value(p.MaxItems.Value);
    }
}
