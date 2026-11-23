using System.Linq;
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

            // A required member the body checks itself is nullable and not `required` here (#937): with the
            // keyword the deserializer refused the request with a 400 naming this record's CLR type, before
            // any rule of the operation could answer its own 422.
            var requiredModifier = prop.IsRequired && !prop.IsPresenceChecked ? "required " : "";
            // Don't add ? if the type already ends with ? (from ToDisplayString)
            var typeAlreadyNullable = prop.TypeName.EndsWith("?");
            var nullableModifier = (prop is { IsNullable: true, IsRequired: false } || prop.IsPresenceChecked) && !typeAlreadyNullable ? "?" : "";

            // The initializer comes across too. Dropping it turned a declared default into null on
            // every request that omitted the field, on a property whose type says it cannot be.
            var initializer = prop.DefaultValueSyntax is { } def ? $" = {def};" : "";

            // The author renamed it on the wire. The attribute sits on their property, which is not this
            // record, so it only reaches the payload if it is written here too.
            if (prop.JsonName is { } jsonName)
                AppendLine($"[global::System.Text.Json.Serialization.JsonPropertyName(\"{jsonName}\")]");

            AppendLine($"public {requiredModifier}{prop.TypeName}{nullableModifier} {prop.Name} {{ get; init; }}{initializer}");
        }

        if (HasPresenceChecks(variant))
            RenderMissingRequiredMembers(variant);

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>Whether the record checks the presence of any member, and so has <c>MissingRequiredMembers()</c>.</summary>
    internal static bool HasPresenceChecks(BodyDtoVariant variant) => variant.Properties.Any(p => p.IsPresenceChecked);

    /// <summary>
    ///     <c>MissingRequiredMembers()</c>: each required member the request left out, as the member's
    ///     <c>[Required]</c> rule reports it (its key, the name it travels under). The binding calls it right
    ///     after the read, where the deserializer used to refuse the request, so the response is the operation's
    ///     422 and not the deserializer's text. Only presence: a member sent empty reaches the operation's own
    ///     rules, which run after its permission check.
    /// </summary>
    private void RenderMissingRequiredMembers(BodyDtoVariant variant)
    {
        const string error = "global::Pragmatic.Validation.Types.ValidationError";
        AppendLine();
        XmlSummary("The required members this request left out, each reported as its required rule reports it.");
        AppendLine($"public {error} MissingRequiredMembers()");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"var error = {error}.Valid;");
        foreach (var prop in variant.Properties.Where(p => p.IsPresenceChecked))
        {
            var key = StringHelper.CSharpLiteral(prop.RequiredMessageKey ?? "validation.required");
            var issue = prop.JsonName is { } wire
                ? $"error.With(new global::Pragmatic.Validation.Types.ValidationIssue(\"{key}\", nameof({prop.Name})) {{ WirePath = \"{StringHelper.CSharpLiteral(wire)}\" }})"
                : $"error.WithFor(nameof({prop.Name}), \"{key}\")";
            AppendLine($"if ({prop.Name} is null)");
            AppendLine($"    error = {issue};");
        }

        AppendLine("return error;");
        DecreaseIndent();
        AppendLine("}");
    }
}
