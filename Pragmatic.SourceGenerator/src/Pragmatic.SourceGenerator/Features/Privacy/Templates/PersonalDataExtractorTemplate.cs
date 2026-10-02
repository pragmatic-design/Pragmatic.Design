using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
/// Generates {Entity}PersonalDataExtractor — the typed projection of an entity's classified fields,
/// used to answer an access request and to build a portability export.
/// </summary>
/// <remarks>
/// Generated rather than reflected for the reason the whole framework avoids reflection: this has to
/// work under AOT, and a projection built by walking properties at runtime cannot be trimmed safely.
/// The second reason matters more here — a generated extractor changes when the entity changes, so an
/// export cannot silently stop including a field somebody added last month.
/// </remarks>
internal sealed class PersonalDataExtractorTemplate : CSharpTemplate
{
    private readonly PrivacyEntityModel _model;
    private readonly string _typeName;

    public PersonalDataExtractorTemplate(PrivacyEntityModel model)
    {
        _model = model;
        _typeName = NamingHelper.AppendSuffix(model.TypeName, "PersonalDataExtractor");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Privacy";
    protected override string? TriggerInfo => $"[PersonalData] on {_model.TypeName}";

    /// <summary>
    /// Nothing to extract means nothing to emit. Two entities with the same simple name in different
    /// namespaces would otherwise share a hint name, and Roslyn answers a duplicate hint by discarding
    /// the generator's entire output behind a warning — hence the namespace argument.
    /// </summary>
    protected override bool Validate() => _model.HasPersonalData;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_typeName, "PrivacyExtract", _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary(
            $"SG-generated projection of the personal data declared on <see cref=\"{_model.TypeName}\"/>. " +
            "Feeds subject access and portability requests.");

        Class(_typeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        XmlSummary("Projects the classified fields of one instance, keyed by property name.");

        AppendLine($"public static IReadOnlyDictionary<string, object?> Extract(global::{_model.FullTypeName} entity)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var values = new Dictionary<string, object?>();");
        AppendLine();

        foreach (var property in _model.Properties)
        {
            if (property.Classification is not { } classification)
                continue;

            // A retained field is still the subject's data and still belongs in an access response —
            // withholding it would answer a different question than the one that was asked.
            // Null-conditional through whatever owns it: a subject whose optional owned record is
            // absent has no value for that field, which is an empty answer and not an exception.
            AppendLine(
                $"values[\"{property.Name}\"] = {property.ReadFrom("entity")};   // {classification.Category}");
        }

        AppendLine();
        AppendLine("return values;");
        DecreaseIndent();
        AppendLine("}");
    }
}
