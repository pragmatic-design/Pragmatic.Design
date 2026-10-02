using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The host reads the languages the modules are translated into from their Translations metadata, and
///     takes a default only when it is decidable.
/// </summary>
public class TheModulesDeclareTheirLanguagesTests
{
    [Fact]
    public void OneModule_ItsCulturesAndItsDefault()
    {
        var languages = Read(Module("""{ "cultures": ["en", "it"], "defaultCulture": "en" }"""));

        languages.Cultures.AsImmutableArray().Should().Equal("en", "it");
        languages.DefaultCulture.Should().Be("en");
    }

    [Fact]
    public void TwoModulesThatAgree_TheUnionAndTheSharedDefault()
    {
        var languages = Read(
            Module("""{ "cultures": ["en", "it"], "defaultCulture": "en" }"""),
            Module("""{ "cultures": ["de", "en"], "defaultCulture": "en" }"""));

        languages.Cultures.AsImmutableArray().Should().Equal("de", "en", "it");
        languages.DefaultCulture.Should().Be("en");
    }

    /// <summary>Two modules written from different languages: which one the application speaks is its call.</summary>
    [Fact]
    public void TwoModulesThatDisagree_NoDefault()
        => Read(
                Module("""{ "cultures": ["en", "it"], "defaultCulture": "en" }"""),
                Module("""{ "cultures": ["it"], "defaultCulture": "it" }"""))
            .DefaultCulture.Should().BeNull();

    /// <summary>A default the module has no file for is not a language it speaks.</summary>
    [Fact]
    public void ADefaultWithoutItsFile_NoDefault()
        => Read(Module("""{ "cultures": ["it", "de"], "defaultCulture": "en" }""")).DefaultCulture.Should().BeNull();

    /// <summary>A module compiled before the default was published declares none: nothing to agree on.</summary>
    [Fact]
    public void AModuleThatDeclaresNoDefault_NoDefault()
        => Read(
                Module("""{ "cultures": ["en"], "defaultCulture": "en" }"""),
                Module("""{ "cultures": ["en"] }"""))
            .DefaultCulture.Should().BeNull();

    /// <summary>The control: no translations, nothing declared.</summary>
    [Fact]
    public void NoTranslations_Nothing()
        => Read().Should().Be(DeclaredLanguagesModel.None);

    private static DeclaredLanguagesModel Read(params AssemblyMetadataModel[] modules)
        => MetadataReader.ExtractDeclaredLanguages([.. modules]);

    private static int _next;

    private static AssemblyMetadataModel Module(string json) => new()
    {
        AssemblyName = $"Module{Interlocked.Increment(ref _next)}",
        Entries = ImmutableArray.Create(new MetadataEntry
        {
            Category = MetadataCategoryIds.Translations,
            SchemaVersion = "1.0.0",
            RegistrationMethod = string.Empty,
            JsonData = json
        })
    };
}
