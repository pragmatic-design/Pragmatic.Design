using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Pragmatic.SourceGenerator.Features.Documents.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     What a module compiles in and the host registers without a line of its own: the provider over its
///     embedded translations, and the assembly its document templates are embedded in.
/// </summary>
public class TheModulesBringTheirResourcesTests
{
    [Fact]
    public void TranslationProviders_OnePerModuleThatEmbeds_InAStableOrder()
    {
        var providers = MetadataReader.ExtractTranslationProviders([
            Module(MetadataCategoryIds.Translations, """{ "cultures": ["en"], "provider": "global::Zeta.TTranslations" }"""),
            Module(MetadataCategoryIds.Translations, """{ "cultures": ["en"] }"""),
            Module(MetadataCategoryIds.Translations, """{ "cultures": ["en"], "provider": "global::Alpha.TTranslations" }"""),
        ]);

        providers.AsImmutableArray().Should().Equal("global::Alpha.TTranslations", "global::Zeta.TTranslations");
    }

    /// <summary>The document the module emits is the document the host reads.</summary>
    [Fact]
    public void PdxTemplateAnchors_ReadFromTheDocumentTheModuleWrites()
    {
        var anchors = MetadataReader.ExtractPdxTemplateAnchors([
            Module(MetadataCategoryIds.PdxTemplates, PdxTemplatesMetadataTemplate.Payload("global::Showcase.Booking.BookingModule")),
            Module(MetadataCategoryIds.PdxTemplates, PdxTemplatesMetadataTemplate.Payload("global::Showcase.Billing.BillingModule")),
        ]);

        anchors.AsImmutableArray().Should().Equal(
            "global::Showcase.Billing.BillingModule", "global::Showcase.Booking.BookingModule");
    }

    /// <summary>The control: a module that declares nothing brings nothing.</summary>
    [Fact]
    public void NoDeclaration_Nothing()
    {
        MetadataReader.ExtractPdxTemplateAnchors([Module(MetadataCategoryIds.Translations, """{ "cultures": ["en"] }""")])
            .Count.Should().Be(0);
        MetadataReader.ExtractTranslationProviders([]).Count.Should().Be(0);
    }

    private static int _next;

    private static AssemblyMetadataModel Module(string category, string json) => new()
    {
        AssemblyName = $"Module{Interlocked.Increment(ref _next)}",
        Entries = ImmutableArray.Create(new MetadataEntry
        {
            Category = category,
            SchemaVersion = "1.0",
            RegistrationMethod = string.Empty,
            JsonData = json,
        }),
    };
}
