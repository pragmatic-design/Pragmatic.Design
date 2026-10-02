using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Manifest.Templates;

/// <summary>
///     Writes a property's <see cref="WireConstraints" /> into the manifest, one JSON Schema keyword
///     each, and only the ones that are set.
/// </summary>
/// <remarks>
///     The keyword names are the schema's own, so the OpenAPI generator copies them onto the
///     property schema without a translation table between the two documents. Absent stays absent: a
///     constraint written as zero would be enforced by a client generator as "the empty string only".
/// </remarks>
internal static class WireConstraintsJson
{
    public static void Write(MetadataJsonBuilder b, WireConstraints constraints)
    {
        if (constraints.MinLength.HasValue) b.Property("minLength").Value(constraints.MinLength.Value);
        if (constraints.MaxLength.HasValue) b.Property("maxLength").Value(constraints.MaxLength.Value);
        if (constraints.Pattern is not null) b.Property("pattern").Value(constraints.Pattern);
        if (constraints.Format is not null) b.Property("format").Value(constraints.Format);
        if (constraints.Minimum.HasValue) b.Property("minimum").Value(constraints.Minimum.Value);
        if (constraints.Maximum.HasValue) b.Property("maximum").Value(constraints.Maximum.Value);
        if (constraints.ExclusiveMinimum.HasValue) b.Property("exclusiveMinimum").Value(constraints.ExclusiveMinimum.Value);
        if (constraints.ExclusiveMaximum.HasValue) b.Property("exclusiveMaximum").Value(constraints.ExclusiveMaximum.Value);
        if (constraints.MinItems.HasValue) b.Property("minItems").Value(constraints.MinItems.Value);
        if (constraints.MaxItems.HasValue) b.Property("maxItems").Value(constraints.MaxItems.Value);
    }
}
