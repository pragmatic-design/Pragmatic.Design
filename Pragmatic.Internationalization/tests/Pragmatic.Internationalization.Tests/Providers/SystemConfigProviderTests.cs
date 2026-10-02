using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     Regression tests for <see cref="SystemConfigProvider"/> (I18N-006).
/// </summary>
public class SystemConfigProviderTests
{
    [Fact]
    public void GetConfiguration_CustomScopesConfigured_FlowsIntoConfig()
    {
        // Regression I18N-006: CustomScopes written on I18NOptions (e.g. via AddScope)
        // must be bridged into the produced I18NConfig instead of being silently dropped.
        // Arrange
        var options = new I18NOptions
        {
            DefaultUICulture = CultureCode.English,
            CustomScopes = new Dictionary<string, CultureCode>
            {
                ["invoicing"] = CultureCode.Italian
            }
        };
        var provider = new SystemConfigProvider(Options.Create(options));

        // Act
        var config = provider.GetConfiguration();

        // Assert
        config.Should().NotBeNull();
        config!.CustomScopes.Should().NotBeNull();
        config.CustomScopes!.Should().ContainKey("invoicing");
        config.CustomScopes!["invoicing"].Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void GetConfiguration_OnlyCustomScopesConfigured_DoesNotReturnNull()
    {
        // Regression I18N-006: CustomScopes alone counts as configuration; the provider
        // must not short-circuit to null when only scopes (and no cultures) are set.
        // Arrange
        var options = new I18NOptions
        {
            CustomScopes = new Dictionary<string, CultureCode>
            {
                ["invoicing"] = CultureCode.Italian
            }
        };
        var provider = new SystemConfigProvider(Options.Create(options));

        // Act
        var config = provider.GetConfiguration();

        // Assert
        config.Should().NotBeNull();
        config!.CustomScopes.Should().ContainKey("invoicing");
    }

    [Fact]
    public void GetConfiguration_NothingConfigured_ReturnsNull()
    {
        // Arrange — fully dynamic scenario, all config from higher-priority providers
        var provider = new SystemConfigProvider(Options.Create(new I18NOptions()));

        // Act
        var config = provider.GetConfiguration();

        // Assert
        config.Should().BeNull();
    }
}
