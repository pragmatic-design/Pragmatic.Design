using Microsoft.Extensions.Configuration;
using Pragmatic.Configuration.Discovery;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Tests.Discovery;

/// <summary>
///     <c>ValidateOnStart</c> asks whether the configuration is complete at the worst possible moment:
///     during host start, in the environment being started. This asks the same question from a test or
///     a build step, against the settings of an environment nobody has deployed to yet.
/// </summary>
public class ConfigurationPreflightTests
{
    private static IConfigurationCatalog Catalog(params ConfigurationSectionDescriptor[] sections)
    {
        var catalog = new ConfigurationCatalog();
        catalog.Contribute(sections);
        return catalog;
    }

    private static ConfigurationSectionDescriptor Section(
        string path, params ConfigurationPropertyDescriptor[] properties) => new()
    {
        SectionPath = path,
        TypeName = $"App.{path.Replace(":", ".")}Options",
        ValidateOnStart = true,
        Properties = properties
    };

    private static ConfigurationPropertyDescriptor Required(string name) => new()
    {
        Name = name, TypeName = "System.String", IsRequired = true
    };

    private static ConfigurationPropertyDescriptor Optional(string name) => new()
    {
        Name = name, TypeName = "System.String"
    };

    private static IConfiguration From(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void MissingRequiredKey_IsNamedWithItsSection()
    {
        var catalog = Catalog(Section("Billing", Required("ApiKey")));

        ConfigurationPreflight.MissingRequiredKeys(From(), catalog)
            .Should().BeEquivalentTo("Billing:ApiKey");
    }

    [Fact]
    public void SuppliedRequiredKey_IsNotReported()
    {
        var catalog = Catalog(Section("Billing", Required("ApiKey")));

        ConfigurationPreflight.MissingRequiredKeys(From(("Billing:ApiKey", "k")), catalog)
            .Should().BeEmpty();
    }

    [Fact]
    public void OptionalKey_IsNeverReported()
    {
        var catalog = Catalog(Section("Billing", Optional("Endpoint")));

        ConfigurationPreflight.MissingRequiredKeys(From(), catalog).Should().BeEmpty();
    }

    // A key supplied as an object rather than a scalar is supplied. GetSection never returns null, so a
    // null check would report every key as present and the whole check would pass always.
    [Fact]
    public void KeySuppliedAsASection_CountsAsSupplied()
    {
        var catalog = Catalog(Section("Billing", Required("Retry")));

        ConfigurationPreflight.MissingRequiredKeys(From(("Billing:Retry:Attempts", "3")), catalog)
            .Should().BeEmpty();
    }

    // Every missing key at once: fixing a deployment one key per run is what makes people stop running
    // the check.
    [Fact]
    public void SeveralMissingKeys_AreAllReportedTogether()
    {
        var catalog = Catalog(
            Section("Billing", Required("ApiKey"), Required("Secret")),
            Section("Mail", Required("Host")));

        var missing = ConfigurationPreflight.MissingRequiredKeys(From(("Billing:ApiKey", "k")), catalog);

        missing.Should().BeEquivalentTo("Billing:Secret", "Mail:Host");

        var act = () => ConfigurationPreflight.ThrowIfIncomplete(From(("Billing:ApiKey", "k")), catalog);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Billing:Secret*Mail:Host*");
    }

    [Fact]
    public void CompleteConfiguration_DoesNotThrow()
    {
        var catalog = Catalog(Section("Billing", Required("ApiKey")));

        var act = () => ConfigurationPreflight.ThrowIfIncomplete(From(("Billing:ApiKey", "k")), catalog);

        act.Should().NotThrow();
    }

    // Composing the same assembly twice — two hosts in one process, a container rebuilt in a test —
    // must not duplicate its sections.
    [Fact]
    public void ContributingTheSameSectionTwice_KeepsOneCopy()
    {
        var catalog = new ConfigurationCatalog();
        var section = Section("Billing", Required("ApiKey"));

        catalog.Contribute([section]);
        catalog.Contribute([section]);

        catalog.Sections.Should().ContainSingle();
    }
}
