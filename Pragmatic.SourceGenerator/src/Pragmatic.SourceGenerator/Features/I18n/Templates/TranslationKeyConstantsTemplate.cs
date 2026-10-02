using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Templates;

/// <summary>
///     <c>{ClassName}Keys</c>: every translation key as a <c>const string</c>, in the hierarchy of
///     <c>{ClassName}</c>.
/// </summary>
/// <remarks>
///     The typed keys are properties — a <c>LocalizationKey</c> or a <c>LocalizedString</c> — and an
///     attribute argument can only be a constant, so a validation rule's <c>MessageKey</c> repeated the key
///     as a string, which a rename of the JSON key orphaned without a word. The constants hold
///     the full key and nothing else: they are for the places a property cannot go.
/// </remarks>
internal sealed class TranslationKeyConstantsTemplate(TranslationKeysGenerationModel model) : CSharpTemplate
{
    private string ClassName => TranslationKeyConstantCatalog.TypeNameFor(model.ClassName);

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/I18n";
    protected override string? SourceInfo => $"{ClassName} from {model.Namespace}";
    protected override string? TriggerInfo => "[TranslationKeys] on assembly";

    public override Artifact RenderOutput() => new($"{model.Namespace}.{ClassName}.g.cs", ToSourceText());

    protected override bool Validate()
        => !string.IsNullOrEmpty(model.Namespace)
           && !string.IsNullOrEmpty(model.ClassName)
           && (model.RootKeys.Count > 0 || model.Groups.Count > 0);

    public override void RenderFile()
    {
        AppendNamespace(model.Namespace);
        AppendLine();

        XmlSummary($"The translation keys of <see cref=\"{model.ClassName}\"/> as constants, for attribute arguments.");
        Class(ClassName, () => RenderBody(model.RootKeys, model.Groups),
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody(EquatableArray<TranslationKeyModel> keys, EquatableArray<TranslationKeyGroupModel> groups)
    {
        foreach (var key in keys)
        {
            XmlSummary($"Key: \"{key.FullKey}\"");
            AppendLine($"public const string {key.PropertyName} = {SymbolDisplay.FormatLiteral(key.FullKey, quote: true)};");
        }

        for (var i = 0; i < groups.Length; i++)
        {
            if (i > 0 || keys.Length > 0)
                AppendLine();

            var group = groups[i];
            XmlSummary($"Translation keys for \"{group.ClassName.ToLowerInvariant()}\", as constants.");
            Class(group.ClassName, () => RenderBody(group.Keys, group.NestedGroups),
                accessModifier: AccessModifier.Public,
                modifiers: new ClassModifiers { IsStatic = true });
        }
    }
}
