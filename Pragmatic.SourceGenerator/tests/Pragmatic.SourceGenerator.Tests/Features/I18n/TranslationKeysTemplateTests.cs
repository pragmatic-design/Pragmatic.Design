using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Pragmatic.SourceGenerator.Features.I18n.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.I18n;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// </summary>
public class TranslationKeysTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_UsesNamespaceAndClassName()
    {
        var model = BuildModel("MyApp.Resources", "T", [BuildKey("user.name", "Name")]);

        var artifact = new TranslationKeysTemplate(model, Config()).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Resources.T.g.cs");
    }

    [Fact]
    public void RenderOutput_ReferenceMode_GeneratesLocalizationKeyProperty()
    {
        var model = BuildModel("MyApp.Resources", "T", [BuildKey("user.name", "Name")]);

        var source = new TranslationKeysTemplate(model, Config(embed: false)).RenderOutput().Text;

        source.Should().Contain("public static class T");
        source.Should().Contain("public static LocalizationKey Name => new(\"user.name\");");
    }

    [Fact]
    public void RenderOutput_EmbedMode_GeneratesLocalizedStringWithTranslation()
    {
        var key = BuildKey("greeting.hello", "Hello",
            translations: ImmutableDictionary<string, string>.Empty.Add("en", "Hello"));
        var model = BuildModel("MyApp.Resources", "T", [key]);

        var source = new TranslationKeysTemplate(model, Config(embed: true)).RenderOutput().Text;

        source.Should().Contain("public static LocalizedString Hello");
        source.Should().Contain("(\"en\", \"Hello\")");
    }

    [Fact]
    public void RenderOutput_EscapesQuotesInEmbeddedValue()
    {
        var key = BuildKey("msg.quote", "Quote",
            translations: ImmutableDictionary<string, string>.Empty.Add("en", "say \"hi\""));
        var model = BuildModel("MyApp.Resources", "T", [key]);

        var source = new TranslationKeysTemplate(model, Config(embed: true)).RenderOutput().Text;

        source.Should().Contain("say \\\"hi\\\"");
    }

    [Fact]
    public void RenderOutput_NoKeys_ProducesEmptyArtifact()
    {
        // Validate() requires at least one root key or group.
        var model = BuildModel("MyApp.Resources", "T", []);

        var artifact = new TranslationKeysTemplate(model, Config()).RenderOutput();

        artifact.Text.Length.Should().Be(0);
    }

    private static TranslationKeysConfiguration Config(bool embed = false) => new()
    {
        ClassName = "T",
        Namespace = "MyApp.Resources",
        EmbedTranslations = embed,
        ByFile = false,
        DefaultCulture = "en"
    };

    private static TranslationKeyModel BuildKey(
        string fullKey,
        string propertyName,
        ImmutableDictionary<string, string>? translations = null) => new(
            fullKey,
            propertyName,
            ImmutableArray<string>.Empty,
            translations: translations ?? ImmutableDictionary<string, string>.Empty);

    private static TranslationKeysGenerationModel BuildModel(
        string ns,
        string className,
        ImmutableArray<TranslationKeyModel> rootKeys) => new(
            ns,
            className,
            rootKeys,
            ImmutableArray<TranslationKeyGroupModel>.Empty,
            ImmutableArray.Create("en"),
            ImmutableArray.Create("common.json"),
            "translations/en/common.json",
            rootKeys.Length);
}
