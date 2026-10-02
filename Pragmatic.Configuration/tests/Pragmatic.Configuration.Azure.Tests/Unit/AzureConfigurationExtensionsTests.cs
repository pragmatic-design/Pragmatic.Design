using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Configuration.Azure.Tests.Unit;

public class AzureConfigurationExtensionsTests
{
    [Fact]
    public void AddAzureAppConfigurationStore_NoEndpointOrConnectionString_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureAppConfigurationStore(opts => { });

        using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<IConfigurationStore>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AppConfigurationEndpoint*");
    }

    [Fact]
    public void AddAzureKeyVaultSecretStore_NoUri_ThrowsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureKeyVaultSecretStore(opts => { });

        using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<ISecretStore>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*KeyVaultUri*");
    }

    [Fact]
    public void AddAzureAppConfigurationStore_WithConnectionString_ResolvesClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureAppConfigurationStore(opts =>
        {
            opts.AppConfigurationConnectionString = "Endpoint=https://test.azconfig.io;Id=abc;Secret=c2VjcmV0";
        });

        using var sp = services.BuildServiceProvider();
        var store = sp.GetService<IConfigurationStore>();
        store.Should().NotBeNull();
    }

    [Fact]
    public void AddAzureConfiguration_RegistersBothStores()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureConfiguration(opts =>
        {
            opts.AppConfigurationConnectionString = "Endpoint=https://test.azconfig.io;Id=abc;Secret=c2VjcmV0";
            opts.KeyVaultUri = "https://test.vault.azure.net";
        });

        using var sp = services.BuildServiceProvider();
        sp.GetService<IConfigurationStore>().Should().NotBeNull();
        // KeyVault requires actual Azure credentials, so we just check registration
        services.Should().Contain(d => d.ServiceType == typeof(ISecretStore));
    }
}
