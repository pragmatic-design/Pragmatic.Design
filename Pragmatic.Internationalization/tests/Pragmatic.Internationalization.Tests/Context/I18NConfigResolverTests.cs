using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

/// <summary>
///     Tests for the I18NConfigResolver provider resolution logic.
/// </summary>
public class I18NConfigResolverTests
{
    #region Test Helpers

    private sealed class TestConfigProvider : II18NConfigProvider
    {
        public int Priority { get; init; }
        public I18NConfig? Config { get; init; }

        public I18NConfig? GetConfiguration() => Config;
    }

    private static TestConfigProvider SystemProvider(
        CultureCode? uiCulture = null,
        CultureCode? dataCulture = null,
        IReadOnlyList<CultureCode>? supported = null) =>
        new()
        {
            Priority = 0,
            Config = new I18NConfig
            {
                DefaultUICulture = uiCulture,
                DefaultDataCulture = dataCulture,
                SupportedCultures = supported
            }
        };

    private static TestConfigProvider TenantProvider(
        CultureCode? uiCulture = null,
        CultureCode? dataCulture = null,
        IReadOnlyList<CultureCode>? supported = null) =>
        new()
        {
            Priority = 100,
            Config = new I18NConfig
            {
                DefaultUICulture = uiCulture,
                DefaultDataCulture = dataCulture,
                SupportedCultures = supported
            }
        };

    private static TestConfigProvider UserProvider(
        CultureCode? uiCulture = null,
        CurrencyCode? preferredCurrency = null) =>
        new()
        {
            Priority = 200,
            Config = new I18NConfig
            {
                DefaultUICulture = uiCulture,
                PreferredCurrency = preferredCurrency
            }
        };

    private static TestConfigProvider RequestProvider(CultureCode? uiCulture = null) =>
        new()
        {
            Priority = 300,
            Config = new I18NConfig { DefaultUICulture = uiCulture }
        };

    #endregion

    #region Basic Resolution

    [Fact]
    public void Resolve_SingleProvider_ReturnsConfig()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian) };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.DefaultUICulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void Resolve_NoProviders_ThrowsI18NConfigurationException()
    {
        var resolver = new I18NConfigResolver([]);

        var act = () => resolver.Resolve();

        act.Should().Throw<I18NConfigurationException>()
            .WithMessage("*UI culture*");
    }

    [Fact]
    public void Resolve_ProviderReturnsNull_ThrowsI18NConfigurationException()
    {
        var providers = new[] { new TestConfigProvider { Priority = 0, Config = null } };
        var resolver = new I18NConfigResolver(providers);

        var act = () => resolver.Resolve();

        act.Should().Throw<I18NConfigurationException>();
    }

    #endregion

    #region Priority-Based Merge

    [Fact]
    public void Resolve_HigherPriorityOverridesLower()
    {
        var providers = new II18NConfigProvider[]
        {
            SystemProvider(CultureCode.English),
            TenantProvider(CultureCode.German),
            UserProvider(CultureCode.Italian)
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        // User (priority 200) overrides Tenant (100) which overrides System (0)
        config.DefaultUICulture.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void Resolve_HigherPriorityWithNull_DefersToLower()
    {
        var providers = new II18NConfigProvider[]
        {
            SystemProvider(uiCulture: CultureCode.English, dataCulture: CultureCode.EnglishUS),
            TenantProvider(uiCulture: CultureCode.German, dataCulture: null)
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        // UI: Tenant overrides System
        config.DefaultUICulture.Should().Be(CultureCode.German);
        // Data: Tenant is null, so System value is used
        config.DefaultDataCulture.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void Resolve_ProvidersAddedInRandomOrder_SortsByPriority()
    {
        // Add providers in non-priority order
        var providers = new II18NConfigProvider[]
        {
            UserProvider(CultureCode.Italian),
            SystemProvider(CultureCode.English),
            TenantProvider(CultureCode.German)
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        // Should still resolve in priority order: System -> Tenant -> User
        config.DefaultUICulture.Should().Be(CultureCode.Italian);
    }

    #endregion

    #region Supported Cultures

    [Fact]
    public void Resolve_SupportedCulturesFromTenant_OverridesSystem()
    {
        var providers = new II18NConfigProvider[]
        {
            SystemProvider(CultureCode.English, supported: [CultureCode.English]),
            TenantProvider(
                CultureCode.German,
                supported: [CultureCode.German, CultureCode.English, CultureCode.Italian])
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.SupportedCultures.Should().HaveCount(3);
        config.SupportedCultures.Should().Contain(CultureCode.German);
        config.SupportedCultures.Should().Contain(CultureCode.Italian);
    }

    [Fact]
    public void IsCultureSupported_ExactMatch_ReturnsTrue()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: [CultureCode.Italian, CultureCode.English]) };
        var resolver = new I18NConfigResolver(providers);

        resolver.IsCultureSupported(CultureCode.Italian).Should().BeTrue();
        resolver.IsCultureSupported(CultureCode.English).Should().BeTrue();
    }

    [Fact]
    public void IsCultureSupported_LanguageMatch_ReturnsTrue()
    {
        // "it" is supported, request "it-IT"
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: [CultureCode.Italian, CultureCode.English]) };
        var resolver = new I18NConfigResolver(providers);

        // it-IT should match because "it" (language only) is supported
        var itIT = CultureCode.FromString("it-IT");
        resolver.IsCultureSupported(itIT).Should().BeTrue();
    }

    [Fact]
    public void IsCultureSupported_NoMatch_ReturnsFalse()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: [CultureCode.Italian, CultureCode.English]) };
        var resolver = new I18NConfigResolver(providers);

        resolver.IsCultureSupported(CultureCode.German).Should().BeFalse();
    }

    [Fact]
    public void IsCultureSupported_NoSupportedCultures_ReturnsTrue()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: null) };
        var resolver = new I18NConfigResolver(providers);

        // When no restrictions, all cultures are supported
        resolver.IsCultureSupported(CultureCode.German).Should().BeTrue();
        resolver.IsCultureSupported(CultureCode.Japanese).Should().BeTrue();
    }

    #endregion

    #region FindBestMatch

    [Fact]
    public void FindBestMatch_ExactMatch_ReturnsExact()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: [CultureCode.Italian, CultureCode.EnglishUS]) };
        var resolver = new I18NConfigResolver(providers);

        var result = resolver.FindBestMatch(CultureCode.EnglishUS);

        result.Should().Be(CultureCode.EnglishUS);
    }

    [Fact]
    public void FindBestMatch_LanguageMatch_ReturnsLanguageMatch()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: [CultureCode.Italian, CultureCode.English]) };
        var resolver = new I18NConfigResolver(providers);

        // Request en-US, supported is "en" (language only)
        var result = resolver.FindBestMatch(CultureCode.EnglishUS);

        result.Should().Be(CultureCode.English);
    }

    [Fact]
    public void FindBestMatch_NoMatch_ReturnsNull()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: [CultureCode.Italian, CultureCode.English]) };
        var resolver = new I18NConfigResolver(providers);

        var result = resolver.FindBestMatch(CultureCode.German);

        result.Should().BeNull();
    }

    [Fact]
    public void FindBestMatch_NoRestrictions_ReturnsRequested()
    {
        var providers = new[] { SystemProvider(CultureCode.Italian, supported: null) };
        var resolver = new I18NConfigResolver(providers);

        var result = resolver.FindBestMatch(CultureCode.German);

        result.Should().Be(CultureCode.German);
    }

    #endregion

    #region Custom Scopes

    [Fact]
    public void Resolve_CustomScopes_MergesFromProviders()
    {
        var providers = new II18NConfigProvider[]
        {
            new TestConfigProvider
            {
                Priority = 0,
                Config = new I18NConfig
                {
                    DefaultUICulture = CultureCode.English,
                    CustomScopes = new Dictionary<string, CultureCode>
                    {
                        ["invoicing"] = CultureCode.German
                    }
                }
            },
            new TestConfigProvider
            {
                Priority = 100,
                Config = new I18NConfig
                {
                    CustomScopes = new Dictionary<string, CultureCode>
                    {
                        ["reporting"] = CultureCode.French
                    }
                }
            }
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.CustomScopes.Should().HaveCount(2);
        config.CustomScopes!["invoicing"].Should().Be(CultureCode.German);
        config.CustomScopes["reporting"].Should().Be(CultureCode.French);
    }

    [Fact]
    public void Resolve_CustomScopes_HigherPriorityOverridesSameKey()
    {
        var providers = new II18NConfigProvider[]
        {
            new TestConfigProvider
            {
                Priority = 0,
                Config = new I18NConfig
                {
                    DefaultUICulture = CultureCode.English,
                    CustomScopes = new Dictionary<string, CultureCode>
                    {
                        ["invoicing"] = CultureCode.German
                    }
                }
            },
            new TestConfigProvider
            {
                Priority = 100,
                Config = new I18NConfig
                {
                    CustomScopes = new Dictionary<string, CultureCode>
                    {
                        ["invoicing"] = CultureCode.French  // Override
                    }
                }
            }
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.CustomScopes!["invoicing"].Should().Be(CultureCode.French);
    }

    #endregion

    #region Currency

    [Fact]
    public void Resolve_PreferredCurrency_OverridesCultureDefault()
    {
        var providers = new II18NConfigProvider[]
        {
            SystemProvider(CultureCode.Italian),
            UserProvider(preferredCurrency: CurrencyCode.CHF)
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.PreferredCurrency.Should().Be(CurrencyCode.CHF);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void Scenario_SimpleStaticSite()
    {
        // Simple blog with static configuration
        var providers = new[]
        {
            SystemProvider(
                CultureCode.Italian,
                dataCulture: CultureCode.Italian,
                supported: [CultureCode.Italian, CultureCode.English])
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.DefaultUICulture.Should().Be(CultureCode.Italian);
        config.DefaultDataCulture.Should().Be(CultureCode.Italian);
        config.SupportedCultures.Should().HaveCount(2);
    }

    [Fact]
    public void Scenario_MultiTenantEnterprise()
    {
        // Enterprise app: System defaults, Tenant overrides supported cultures, User overrides UI
        var providers = new II18NConfigProvider[]
        {
            SystemProvider(
                CultureCode.EnglishUS,
                dataCulture: CultureCode.EnglishUS,
                supported: [CultureCode.EnglishUS]),
            TenantProvider(
                CultureCode.German,
                supported: [CultureCode.German, CultureCode.EnglishUS, CultureCode.Italian]),
            UserProvider(CultureCode.Italian)
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        // User preference for UI
        config.DefaultUICulture.Should().Be(CultureCode.Italian);
        // Data from System (not overridden)
        config.DefaultDataCulture.Should().Be(CultureCode.EnglishUS);
        // Supported from Tenant
        config.SupportedCultures.Should().HaveCount(3);
    }

    [Fact]
    public void Scenario_RequestBasedLanguage()
    {
        // Request-based language detection from Accept-Language
        var providers = new II18NConfigProvider[]
        {
            SystemProvider(
                CultureCode.EnglishUS,
                supported: [CultureCode.EnglishUS, CultureCode.Italian, CultureCode.German]),
            RequestProvider(CultureCode.Italian)  // From Accept-Language header
        };
        var resolver = new I18NConfigResolver(providers);

        var config = resolver.Resolve();

        config.DefaultUICulture.Should().Be(CultureCode.Italian);
    }

    #endregion
}
