// =============================================================================
// Pragmatic.Internationalization - TranslationKeysTemplate
// Template for generating strongly-typed translation keys with embedded values
// =============================================================================

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Templates;

/// <summary>
///     Template for generating strongly-typed translation keys from JSON files.
///     Supports both LocalizedString (embedded) and LocalizationKey (reference) modes.
/// </summary>
internal sealed class TranslationKeysTemplate : CSharpTemplate
{
    private readonly TranslationKeysConfiguration _config;
    private readonly TranslationKeysGenerationModel _model;

    public TranslationKeysTemplate(
        TranslationKeysGenerationModel model,
        TranslationKeysConfiguration config)
    {
        _model = model;
        _config = config;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/I18n";
    protected override string? SourceInfo => $"{_model.ClassName} from {_model.Namespace}";
    protected override string? TriggerInfo => "[TranslationKeys] on assembly";

    public override Artifact RenderOutput()
    {
        var hintName = $"{_model.Namespace}.{_model.ClassName}.g.cs";
        return new Artifact(hintName, ToSourceText());
    }

    protected override bool Validate()
    {
        return !string.IsNullOrEmpty(_model.Namespace) &&
               !string.IsNullOrEmpty(_model.ClassName) &&
               (_model.RootKeys.Length > 0 || _model.Groups.Length > 0);
    }

    public override void RenderFile()
    {
        // Both render modes use types from the same namespace:
        // LocalizedString (embedded) and LocalizationKey (key-reference).
        AddUsing("Pragmatic.Internationalization.Types");

        AppendNamespace(_model.Namespace);
        AppendLine();

        RenderRootClass();
    }

    private void RenderRootClass()
    {
        var culturesInfo = _model.Cultures.Length > 0
            ? $" Cultures: {string.Join(", ", _model.Cultures)}."
            : "";

        XmlSummary($"Strongly-typed translation keys.{culturesInfo}");

        var modifiers = new ClassModifiers { IsStatic = true };

        Class(_model.ClassName, RenderRootBody,
            accessModifier: AccessModifier.Public,
            modifiers: modifiers);
    }

    private void RenderRootBody()
    {
        // Render root-level keys
        foreach (var key in _model.RootKeys)
            RenderKey(key);

        if (_model.RootKeys.Length > 0 && _model.Groups.Length > 0)
            AppendLine();

        // Render nested groups
        for (var i = 0; i < _model.Groups.Length; i++)
        {
            RenderGroup(_model.Groups[i]);
            if (i < _model.Groups.Length - 1)
                AppendLine();
        }
    }

    private void RenderKey(TranslationKeyModel key)
    {
        // Add XML documentation
        RenderKeyDocumentation(key);

        if (_config.EmbedTranslations && key.HasEmbeddedTranslations)
            RenderEmbeddedKey(key);
        else
            RenderKeyReferenceProperty(key);
    }

    private void RenderKeyDocumentation(TranslationKeyModel key)
    {
        var summary = $"Key: \"{key.FullKey}\"";
        if (!string.IsNullOrEmpty(key.SampleValue))
            summary += $" - Sample: \"{EscapeXml(key.SampleValue!)}\"";

        XmlSummary(summary);

        // Include translations in remarks if embedded
        if (_config.EmbedTranslations && key is { HasEmbeddedTranslations: true, Translations.Count: > 1 })
        {
            AppendLine("/// <remarks>");
            AppendLine("/// Translations:");
            foreach (var kvp in key.Translations.OrderBy(t => t.Key == _config.DefaultCulture ? 0 : 1)
                         .ThenBy(t => t.Key))
            {
                var escapedValue = EscapeXml(TruncateForDoc(kvp.Value, 80));
                AppendLine($"/// <para><b>{kvp.Key}</b>: {escapedValue}</para>");
            }

            AppendLine("/// </remarks>");
        }
    }

    private void RenderEmbeddedKey(TranslationKeyModel key)
    {
        var translations = key.Translations
            .OrderBy(t => t.Key == _config.DefaultCulture ? 0 : 1)
            .ThenBy(t => t.Key)
            .ToList();

        if (translations.Count == 1)
        {
            var first = translations[0];
            AppendLine(
                $"public static LocalizedString {key.PropertyName} => LocalizedString.From((\"{first.Key}\", \"{EscapeString(first.Value)}\"));");
        }
        else
        {
            AppendLine($"public static LocalizedString {key.PropertyName} => LocalizedString.From(");
            IncreaseIndent();
            for (var i = 0; i < translations.Count; i++)
            {
                var kvp = translations[i];
                var comma = i < translations.Count - 1 ? "," : ");";
                AppendLine($"(\"{kvp.Key}\", \"{EscapeString(kvp.Value)}\"){comma}");
            }

            DecreaseIndent();
        }

        AppendLine();
    }

    private void RenderKeyReferenceProperty(TranslationKeyModel key)
    {
        AppendLine($"public static LocalizationKey {key.PropertyName} => new(\"{key.FullKey}\");");
        AppendLine();
    }

    private void RenderGroup(TranslationKeyGroupModel group)
    {
        XmlSummary($"Translation keys for \"{group.ClassName.ToLowerInvariant()}\".");

        var modifiers = new ClassModifiers { IsStatic = true };

        Class(group.ClassName, () => RenderGroupBody(group),
            accessModifier: AccessModifier.Public,
            modifiers: modifiers);
    }

    private void RenderGroupBody(TranslationKeyGroupModel group)
    {
        // Render keys in this group
        foreach (var key in group.Keys)
            RenderKey(key);

        // Render nested groups
        for (var i = 0; i < group.NestedGroups.Length; i++)
        {
            RenderGroup(group.NestedGroups[i]);
            if (i < group.NestedGroups.Length - 1)
                AppendLine();
        }
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string EscapeString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string TruncateForDoc(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength - 3) + "...";
    }
}
