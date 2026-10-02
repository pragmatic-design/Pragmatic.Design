using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Templates;

/// <summary>
///     <c>{ClassName}Translations</c>: an <c>ILocalizationProvider</c> over the translations
///     <c>[TranslationKeys(EmbedTranslations = true)]</c> compiled into the module.
/// </summary>
/// <remarks>
///     <para>
///         Embedding alone reaches the typed properties of <c>{ClassName}</c> and nothing else. Every lookup
///         by key — <c>IStringLocalizer</c>, and with it <c>t:</c> in a template — asks the runtime
///         providers, which without this one have never been given the module's files, and comes back with
///         the key: a mail made of keys and no complaint. The module declares this provider in its
///         translations metadata and the host registers it, so a module that embeds its translations needs
///         nothing copied beside the host.
///     </para>
///     <para>
///         Plural forms are not carried: the embedded model holds one string per key and culture.
///     </para>
/// </remarks>
internal sealed class EmbeddedTranslationsTemplate(TranslationKeysGenerationModel model) : CSharpTemplate
{
    private const string Provider = "global::Pragmatic.Internationalization.Providers.ILocalizationProvider";
    private const string Plural = "global::Pragmatic.Internationalization.Types.PluralString";
    private const string Table = "global::System.Collections.Generic.Dictionary<string, string>";

    /// <summary>The provider's type name for a translations class: <c>T</c> → <c>TTranslations</c>.</summary>
    public static string TypeNameFor(string className) => className + "Translations";

    private string ClassName => TypeNameFor(model.ClassName);

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/I18n";
    protected override string? SourceInfo => $"{ClassName} from {model.Namespace}";
    protected override string? TriggerInfo => "[TranslationKeys(EmbedTranslations = true)] on assembly";

    public override Artifact RenderOutput() => new($"{model.Namespace}.{ClassName}.g.cs", ToSourceText());

    protected override bool Validate()
        => !string.IsNullOrEmpty(model.Namespace) && AllKeys().Any(k => k.HasEmbeddedTranslations);

    public override void RenderFile()
    {
        AppendNamespace(model.Namespace);
        AppendLine();

        XmlSummary($"The translations embedded in this module, as a localization provider the host registers.");
        AppendLine($"public sealed class {ClassName} : {Provider}");
        Block(() =>
        {
            AppendLine($"private static readonly {Table} Nothing = new();");
            AppendLine($"private static readonly global::System.Collections.Generic.Dictionary<string, {Plural}> NoPlurals = new();");
            AppendLine();

            AppendLine($"private static readonly global::System.Collections.Generic.Dictionary<string, {Table}> ByCulture =");
            IncreaseIndent();
            AppendLine("new(global::System.StringComparer.OrdinalIgnoreCase)");
            AppendLine("{");
            IncreaseIndent();
            foreach (var culture in Cultures())
            {
                AppendLine($"[{Literal(culture)}] = new(global::System.StringComparer.Ordinal)");
                AppendLine("{");
                IncreaseIndent();
                foreach (var key in AllKeys())
                    if (key.Translations.TryGetValue(culture, out var value))
                        AppendLine($"[{Literal(key.FullKey)}] = {Literal(value)},");
                DecreaseIndent();
                AppendLine("},");
            }
            DecreaseIndent();
            AppendLine("};");
            DecreaseIndent();
            AppendLine();

            AppendLine("/// <inheritdoc />");
            AppendLine("public global::System.Collections.Generic.IReadOnlyList<string> SupportedCultures { get; } = [.. ByCulture.Keys];");
            AppendLine();
            AppendLine("/// <inheritdoc />");
            AppendLine("public string? GetString(string key, string culture)");
            IncreaseIndent();
            AppendLine("=> ByCulture.TryGetValue(culture, out var table) && table.TryGetValue(key, out var value) ? value : null;");
            DecreaseIndent();
            AppendLine();
            AppendLine("/// <inheritdoc />");
            AppendLine($"public {Plural}? GetPlural(string key, string culture) => null;");
            AppendLine();
            AppendLine("/// <inheritdoc />");
            AppendLine("public global::System.Collections.Generic.IReadOnlyDictionary<string, string> GetAll(string culture)");
            IncreaseIndent();
            AppendLine("=> ByCulture.TryGetValue(culture, out var table) ? table : Nothing;");
            DecreaseIndent();
            AppendLine();
            AppendLine("/// <inheritdoc />");
            AppendLine($"public global::System.Collections.Generic.IReadOnlyDictionary<string, {Plural}> GetAllPlurals(string culture) => NoPlurals;");
        });
    }

    private IEnumerable<string> Cultures()
        => AllKeys().SelectMany(k => k.Translations.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal);

    private IEnumerable<TranslationKeyModel> AllKeys()
    {
        foreach (var key in model.RootKeys)
            yield return key;
        foreach (var group in model.Groups)
            foreach (var key in KeysOf(group))
                yield return key;
    }

    private static IEnumerable<TranslationKeyModel> KeysOf(TranslationKeyGroupModel group)
    {
        foreach (var key in group.Keys)
            yield return key;
        foreach (var nested in group.NestedGroups)
            foreach (var key in KeysOf(nested))
                yield return key;
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
