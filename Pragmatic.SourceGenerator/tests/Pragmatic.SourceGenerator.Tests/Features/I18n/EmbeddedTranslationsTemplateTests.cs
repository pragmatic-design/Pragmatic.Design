using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Pragmatic.SourceGenerator.Features.I18n.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.I18n;

/// <summary>
///     <c>[TranslationKeys(EmbedTranslations = true)]</c> puts the translations in the compiled module, and
///     the runtime reads them through an <c>ILocalizationProvider</c> the generator writes beside
///     <c>{ClassName}</c>. Before it, the embedded strings reached only the typed properties: every lookup by
///     key — <c>IStringLocalizer</c>, and with it <c>t:</c> in a template — came back with the key.
/// </summary>
public class EmbeddedTranslationsTemplateTests
{
    [Fact]
    public void Embedded_ModuleGetsAProviderOverEveryKeyAndCulture()
    {
        var model = BuildModel(
            rootKeys: [Key("mail.subject", "Subject", ("en", "Your booking"), ("it", "La tua prenotazione"))],
            groups:
            [
                new TranslationKeyGroupModel("Invoice",
                    [Key("invoice.title", "Title", ("en", "Invoice"), ("it", "Fattura"))],
                    ImmutableArray<TranslationKeyGroupModel>.Empty),
            ]);

        var source = new EmbeddedTranslationsTemplate(model).RenderOutput().Text;

        source.Should().Contain("public sealed class TTranslations : global::Pragmatic.Internationalization.Providers.ILocalizationProvider");
        source.Should().Contain("[\"mail.subject\"] = \"La tua prenotazione\"");
        source.Should().Contain("[\"invoice.title\"] = \"Fattura\"");
        source.Should().Contain("[\"invoice.title\"] = \"Invoice\"");
    }

    [Fact]
    public void Embedded_ValuesAreEscaped()
    {
        var model = BuildModel(rootKeys: [Key("msg.quote", "Quote", ("en", "say \"hi\"\n"))]);

        var source = new EmbeddedTranslationsTemplate(model).RenderOutput().Text;

        source.Should().Contain("[\"msg.quote\"] = \"say \\\"hi\\\"\\n\"");
    }

    [Fact]
    public void NothingEmbedded_NoProvider()
    {
        var model = BuildModel(rootKeys: [Key("mail.subject", "Subject")]);

        new EmbeddedTranslationsTemplate(model).RenderOutput().IsEmpty.Should().BeTrue();
    }

    private static TranslationKeyModel Key(string fullKey, string property, params (string Culture, string Value)[] values)
        => new(fullKey, property, ImmutableArray<string>.Empty,
            translations: values.ToImmutableDictionary(v => v.Culture, v => v.Value));

    private static TranslationKeysGenerationModel BuildModel(
        ImmutableArray<TranslationKeyModel> rootKeys,
        ImmutableArray<TranslationKeyGroupModel>? groups = null) => new(
            "Showcase.Booking",
            "T",
            rootKeys,
            groups ?? ImmutableArray<TranslationKeyGroupModel>.Empty,
            ImmutableArray.Create("en", "it"),
            ImmutableArray.Create("en.json", "it.json"),
            "translations/en.json",
            rootKeys.Length);
}
