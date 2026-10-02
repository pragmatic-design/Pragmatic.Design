using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Renders one request-body record. Shared by the versioned and unversioned templates.
/// </summary>
/// <remarks>
///     One renderer, because two copies of the same lines drift: a change that carries a property's
///     declared initializer across to the body has to reach both, or a versioned endpoint turns a
///     declared default into <c>null</c>.
/// </remarks>
internal abstract class BodyDtoTemplateBase : CSharpTemplate
{
    /// <summary>Emits the record for a single variant, without the surrounding namespace.</summary>
    protected void RenderVariant(BodyDtoVariant variant)
    {
        // A partial record, so the Validation SG can add the ISyncValidator implementation.
        AppendLine($"public partial record {variant.Name}");
        AppendLine("{");
        IncreaseIndent();

        foreach (var prop in variant.Properties)
        {
            if (!string.IsNullOrEmpty(prop.Summary))
                XmlSummary(prop.Summary!);

            var requiredModifier = prop.IsRequired ? "required " : "";
            // Don't add ? if the type already ends with ? (from ToDisplayString)
            var typeAlreadyNullable = prop.TypeName.EndsWith("?");
            var nullableModifier = prop is { IsNullable: true, IsRequired: false } && !typeAlreadyNullable ? "?" : "";

            // The initializer comes across too. Dropping it turned a declared default into null on
            // every request that omitted the field, on a property whose type says it cannot be.
            var initializer = prop.DefaultValueSyntax is { } def ? $" = {def};" : "";

            // The author renamed it on the wire. The attribute sits on their property, which is not this
            // record, so it only reaches the payload if it is written here too.
            if (prop.JsonName is { } jsonName)
                AppendLine($"[global::System.Text.Json.Serialization.JsonPropertyName(\"{jsonName}\")]");

            AppendLine($"public {requiredModifier}{prop.TypeName}{nullableModifier} {prop.Name} {{ get; init; }}{initializer}");
        }

        DecreaseIndent();
        AppendLine("}");
    }
}
