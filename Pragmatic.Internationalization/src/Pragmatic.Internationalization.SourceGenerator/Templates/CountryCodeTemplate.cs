// =============================================================================
// Pragmatic.Internationalization - CountryCodeTemplate
// CSharpTemplate for generating CountryCode static properties
// =============================================================================

using Pragmatic.Internationalization.SourceGenerator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Internationalization.SourceGenerator.Templates;

/// <summary>
///     Generates the CountryCode partial struct with ISO 3166-1 country codes.
/// </summary>
internal sealed class CountryCodeTemplate(CountryCodeGenerationModel model) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Internationalization";

    public override Artifact RenderOutput()
    {
        return new Artifact("CountryCode.g.cs", ToSourceText());
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

        RenderCountryCodeStruct();
    }

    private void RenderCountryCodeStruct()
    {
        Struct("CountryCode", RenderBody,
            null,
            AccessModifier.Public,
            new ClassModifiers { Partial = true, IsReadOnly = true });
    }

    private void RenderBody()
    {
        RenderCountryFields();
        AppendLine();
        RenderAllProperty();
        AppendLine();
        RenderLookup();
    }

    // =========================================================================
    // Country Fields
    // =========================================================================

    private void RenderCountryFields()
    {
        Comment("=============================================================================");
        Comment("ISO 3166-1 Country Codes");
        Comment("=============================================================================");
        AppendLine();

        foreach (var country in model.Countries.OrderBy(c => c.Code))
        {
            XmlSummary($"{country.EscapedName} ({country.Code})");
            AppendLine($"public static readonly CountryCode {country.PropertyName} = " +
                       $"new(\"{country.Code}\", \"{country.EscapedName}\", \"{country.EscapedNativeName}\", " +
                       $"LanguageCode.FromCode(\"{country.DefaultLanguage}\"), " +
                       $"CurrencyCode.FromCode(\"{country.DefaultCurrency}\"), " +
                       $"\"{country.DateFormat}\", '{country.DecimalSeparator}', '{country.EscapedGroupSeparator}');");
            AppendLine();
        }
    }

    // =========================================================================
    // All Property
    // =========================================================================

    private void RenderAllProperty()
    {
        Comment("=============================================================================");
        Comment("All Countries Collection");
        Comment("=============================================================================");
        AppendLine();

        AppendLine("private static readonly CountryCode[] _all =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var country in model.Countries.OrderBy(c => c.Code))
            AppendLine($"{country.PropertyName},");

        DecreaseIndent();
        AppendLine("];");
        AppendLine();

        XmlSummary("Gets all supported ISO 3166-1 country codes.");
        AppendLine("public static IReadOnlyList<CountryCode> All => _all;");
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

        AppendLine("private static readonly Dictionary<string, CountryCode> _lookup = " +
                   "new(StringComparer.OrdinalIgnoreCase)");
        AppendLine("{");
        IncreaseIndent();

        foreach (var country in model.Countries.OrderBy(c => c.Code))
            AppendLine($"[\"{country.Code}\"] = {country.PropertyName},");

        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        RenderTryFromCodeInternal();
    }

    private void RenderTryFromCodeInternal()
    {
        // Partial method implementation - must match declaration in CountryCode.cs
        AppendLine(
            "private static partial void TryFromCodeInternal(string code, out CountryCode country, out bool found)");
        Block(() => { AppendLine("found = _lookup.TryGetValue(code, out country);"); });
    }
}
