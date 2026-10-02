using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

using static Pragmatic.SourceGenerator.Core.TemplateHelpers;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates a partial class on the user entity with:
///     - ToProfile() method returning <c>IUserProfile</c>
///     - A companion profile record implementing <c>IUserProfile</c>
/// </summary>
internal sealed class UserProfileTemplate : CSharpTemplate
{
    private readonly UserEntityModel _model;

    public UserProfileTemplate(UserEntityModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Profile", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => !string.IsNullOrEmpty(_model.TypeName);

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Identity");

        if (!string.IsNullOrEmpty(_model.Namespace))
            AppendNamespace(_model.Namespace);

        AppendLine();

        // Partial class with ToProfile() method
        XmlSummary("Generated profile support.");
        Class(_model.TypeName, RenderPartialBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });

        AppendLine();

        // Companion profile record
        RenderProfileRecord();
    }

    private void RenderPartialBody()
    {
        var profileTypeName = NamingHelper.AppendSuffix(_model.TypeName, "Profile");

        XmlSummary("Creates a snapshot of this user's profile data.");
        Method("ToProfile", () =>
        {
            var wellKnown = _model.ProfileProperties.Where(p => p.IsWellKnown).ToList();
            var custom = _model.ProfileProperties.Where(p => !p.IsWellKnown).ToList();

            AppendLine($"return new {profileTypeName}");
            AppendLine("{");
            IncreaseIndent();

            // Well-known properties
            var preferredCulture = wellKnown.FirstOrDefault(p => p.Name == "PreferredCulture");
            AppendLine(preferredCulture is not null
                ? $"PreferredCulture = this.{preferredCulture.Name},"
                : "PreferredCulture = null,");

            var timeZone = wellKnown.FirstOrDefault(p => p.Name == "TimeZone");
            AppendLine(timeZone is not null
                ? $"TimeZone = this.{timeZone.Name},"
                : "TimeZone = null,");

            // Custom properties dictionary
            if (custom.Count > 0)
            {
                AppendLine("Properties = new Dictionary<string, string?>");
                AppendLine("{");
                IncreaseIndent();
                foreach (var prop in custom)
                {
                    AppendLine($"[\"{prop.Name}\"] = this.{prop.Name}?.ToString(),");
                }
                DecreaseIndent();
                AppendLine("}");
            }
            else
            {
                AppendLine("Properties = new Dictionary<string, string?>()");
            }

            DecreaseIndent();
            AppendLine("};");
        }, "IUserProfile", accessModifier: AccessModifier.Public);
    }

    private void RenderProfileRecord()
    {
        var profileTypeName = NamingHelper.AppendSuffix(_model.TypeName, "Profile");

        XmlSummary($"Immutable profile snapshot for <see cref=\"{_model.TypeName}\"/>.");
        AppendLine($"{_model.Accessibility} sealed record {profileTypeName} : IUserProfile");
        AppendLine("{");
        IncreaseIndent();

        AppendLine("/// <inheritdoc />");
        AppendLine("public string? PreferredCulture { get; init; }");
        AppendLine();
        AppendLine("/// <inheritdoc />");
        AppendLine("public string? TimeZone { get; init; }");
        AppendLine();
        AppendLine("/// <inheritdoc />");
        AppendLine("public required IReadOnlyDictionary<string, string?> Properties { get; init; }");

        DecreaseIndent();
        AppendLine("}");
    }
}
