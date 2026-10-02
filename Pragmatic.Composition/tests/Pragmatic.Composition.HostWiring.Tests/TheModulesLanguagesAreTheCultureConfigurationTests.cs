// Pragmatic.Composition.HostWiring.Tests - The languages the modules are translated into

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host whose module is translated registers the module's languages as the culture configuration an
///     application that configures none gets; a host with no translations registers nothing.
/// </summary>
/// <remarks>
///     The probe library carries one translation file, <c>translations/en.json</c>, and declares no
///     <c>[TranslationKeys]</c>: the culture it is written from is the attribute's default, <c>en</c>.
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class TheModulesLanguagesAreTheCultureConfigurationTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    private const string DeclaredLanguages =
        "new global::Pragmatic.Internationalization.AspNetCore.Providers.DeclaredLanguagesConfigProvider(\"en\", [\"en\"]));";

    [Fact]
    public void ATranslatedModule_GivesTheHostItsLanguages()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(DeclaredLanguages,
            $"the probe library is translated into en, written from en; Host.Services.g.cs has {lines.Count} lines");
        fixture.ControlErrors.Should().BeEmpty("the registration has to compile against the real package");
    }

    /// <summary>The control: nothing translated, nothing declared.</summary>
    [Fact]
    public void AHostWithNoTranslations_RegistersNoLanguages()
        => string.Join("\n", HostWiringFixture.LinesOf(fixture.Bare, HostServices))
            .Should().NotContain("DeclaredLanguagesConfigProvider");
}
