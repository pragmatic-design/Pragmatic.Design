// =============================================================================
// Pragmatic.Internationalization - CurrencyCodeTemplate
// Template for generating CurrencyCode static properties
// =============================================================================

using Pragmatic.Internationalization.SourceGenerator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Internationalization.SourceGenerator.Templates;

/// <summary>
///     Template for generating CurrencyCode static properties from ISO 4217 data.
/// </summary>
internal sealed class CurrencyCodeTemplate(CurrencyCodeGenerationModel model) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Internationalization";

    public override Artifact RenderOutput()
    {
        return new Artifact("CurrencyCode.g.cs", ToSourceText());
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

        RenderCurrencyCodeStruct();
    }

    private void RenderCurrencyCodeStruct()
    {
        Struct("CurrencyCode", RenderBody,
            null,
            AccessModifier.Public,
            new ClassModifiers { Partial = true, IsReadOnly = true });
    }

    private void RenderBody()
    {
        RenderCurrencyFields();
        AppendLine();
        RenderAllProperty();
        AppendLine();
        RenderLookup();
    }

    // =========================================================================
    // Currency Fields
    // =========================================================================

    private void RenderCurrencyFields()
    {
        Comment("=============================================================================");
        Comment("ISO 4217 Currency Codes");
        Comment("=============================================================================");
        AppendLine();

        foreach (var currency in model.Currencies.OrderBy(c => c.Code))
        {
            XmlSummary($"{currency.EscapedName} ({currency.Code})");
            AppendLine($"public static readonly CurrencyCode {currency.Code} = " +
                       $"new(\"{currency.Code}\", \"{currency.EscapedName}\", \"{currency.EscapedSymbol}\", {currency.MinorUnits});");
            AppendLine();
        }
    }

    // =========================================================================
    // All Property
    // =========================================================================

    private void RenderAllProperty()
    {
        Comment("=============================================================================");
        Comment("All Currencies Collection");
        Comment("=============================================================================");
        AppendLine();

        AppendLine("private static readonly CurrencyCode[] _all =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var currency in model.Currencies.OrderBy(c => c.Code))
            AppendLine($"{currency.Code},");

        DecreaseIndent();
        AppendLine("];");
        AppendLine();

        XmlSummary("Gets all supported ISO 4217 currency codes.");
        AppendLine("public static IReadOnlyList<CurrencyCode> All => _all;");
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

        AppendLine("private static readonly Dictionary<string, CurrencyCode> _lookup = " +
                   "new(StringComparer.OrdinalIgnoreCase)");
        AppendLine("{");
        IncreaseIndent();

        foreach (var currency in model.Currencies.OrderBy(c => c.Code))
            AppendLine($"[\"{currency.Code}\"] = {currency.Code},");

        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        RenderTryFromCodeInternal();
    }

    private void RenderTryFromCodeInternal()
    {
        // Partial method implementation - must match declaration in CurrencyCode.cs
        AppendLine(
            "private static partial void TryFromCodeInternal(string code, out CurrencyCode currency, out bool found)");
        Block(() => { AppendLine("found = _lookup.TryGetValue(code, out currency);"); });
    }
}