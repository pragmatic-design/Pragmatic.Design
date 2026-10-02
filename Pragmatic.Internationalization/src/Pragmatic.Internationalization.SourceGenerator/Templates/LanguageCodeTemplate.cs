// =============================================================================
// Pragmatic.Internationalization - LanguageCodeTemplate
// CSharpTemplate for generating LanguageCode static properties
// =============================================================================

using Pragmatic.Internationalization.SourceGenerator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Internationalization.SourceGenerator.Templates;

/// <summary>
///     Generates the LanguageCode partial struct with ISO 639-1 language codes.
/// </summary>
internal sealed class LanguageCodeTemplate(LanguageCodeGenerationModel model) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Internationalization";

    public override Artifact RenderOutput()
    {
        return new Artifact("LanguageCode.g.cs", ToSourceText());
    }

    protected override bool Validate()
    {
        return model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");

        AppendNamespace("Pragmatic.Internationalization.Types");
        AppendLine();

        RenderLanguageCodeStruct();
    }

    private void RenderLanguageCodeStruct()
    {
        Struct("LanguageCode", RenderBody,
            null,
            AccessModifier.Public,
            new ClassModifiers { Partial = true, IsReadOnly = true });
    }

    private void RenderBody()
    {
        RenderLanguageFields();
        AppendLine();
        RenderAllProperty();
        AppendLine();
        RenderLookup();
    }

    // =========================================================================
    // Language Fields
    // =========================================================================

    private void RenderLanguageFields()
    {
        Comment("=============================================================================");
        Comment("ISO 639-1 Language Codes");
        Comment("=============================================================================");
        AppendLine();

        foreach (var lang in model.Languages.OrderBy(l => l.Code))
        {
            XmlSummary($"{lang.EscapedName} ({lang.Code}){(lang.IsRightToLeft ? " - RTL" : "")}");
            AppendLine($"public static readonly LanguageCode {lang.PropertyName} = " +
                       $"new(\"{lang.Code}\", \"{lang.EscapedName}\", \"{lang.EscapedNativeName}\", " +
                       $"{lang.IsRightToLeft.ToString().ToLowerInvariant()}, PluralRuleFamily.{lang.PluralFamily});");
            AppendLine();
        }
    }

    // =========================================================================
    // All Property
    // =========================================================================

    private void RenderAllProperty()
    {
        Comment("=============================================================================");
        Comment("All Languages Collection");
        Comment("=============================================================================");
        AppendLine();

        AppendLine("private static readonly LanguageCode[] _all =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var lang in model.Languages.OrderBy(l => l.Code))
            AppendLine($"{lang.PropertyName},");

        DecreaseIndent();
        AppendLine("];");
        AppendLine();

        XmlSummary("Gets all supported ISO 639-1 language codes.");
        AppendLine("public static IReadOnlyList<LanguageCode> All => _all;");
    }

    // =========================================================================
    // Lookup
    // =========================================================================

    private void RenderLookup()
    {
        Comment("=============================================================================");
        Comment("Lookup Implementation");
        Comment("=============================================================================");
        AppendLine();

        AppendLine("private static readonly Dictionary<string, LanguageCode> _lookup = " +
                   "new(StringComparer.OrdinalIgnoreCase)");
        AppendLine("{");
        IncreaseIndent();

        foreach (var lang in model.Languages.OrderBy(l => l.Code))
            AppendLine($"[\"{lang.Code}\"] = {lang.PropertyName},");

        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        RenderTryFromCodeInternal();
    }

    private void RenderTryFromCodeInternal()
    {
        // Partial method implementation - must match declaration in LanguageCode.cs
        AppendLine(
            "private static partial void TryFromCodeInternal(string code, out LanguageCode language, out bool found)");
        Block(() => { AppendLine("found = _lookup.TryGetValue(code, out language);"); });
    }
}
