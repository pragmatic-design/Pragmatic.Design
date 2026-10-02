using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Azure.Tests.Unit;

public class AzureConfigurationOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new AzureConfigurationOptions();

        options.KeyPrefix.Should().Be("Pragmatic");
        options.SentinelKey.Should().Be("Pragmatic:Sentinel");
        options.CacheExpiration.Should().Be(TimeSpan.FromSeconds(30));
        options.SecretCacheExpiration.Should().Be(TimeSpan.FromMinutes(5));
        options.AppConfigurationEndpoint.Should().BeNull();
        options.AppConfigurationConnectionString.Should().BeNull();
        options.KeyVaultUri.Should().BeNull();
    }
}
