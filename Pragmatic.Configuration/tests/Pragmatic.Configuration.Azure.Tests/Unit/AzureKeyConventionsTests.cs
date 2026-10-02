using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Azure;

namespace Pragmatic.Configuration.Azure.Tests.Unit;

public class AzureKeyConventionsTests
{
    [Fact]
    public void ToAppConfigKey_FormatsCorrectly()
    {
        var key = AzureKeyConventions.ToAppConfigKey("Pragmatic", "App:Timeout");
        key.Should().Be("Pragmatic:App:Timeout");
    }

    [Fact]
    public void FromAppConfigKey_ExtractsOriginalKey()
    {
        var key = AzureKeyConventions.FromAppConfigKey("Pragmatic", "Pragmatic:App:Timeout");
        key.Should().Be("App:Timeout");
    }

    [Fact]
    public void FromAppConfigKey_NoPrefix_ReturnsAsIs()
    {
        var key = AzureKeyConventions.FromAppConfigKey("Pragmatic", "Other:Key");
        key.Should().Be("Other:Key");
    }

    [Fact]
    public void ToAppConfigKeyFilter_FormatsCorrectly()
    {
        var filter = AzureKeyConventions.ToAppConfigKeyFilter("Pragmatic", "App:Database:");
        filter.Should().Be("Pragmatic:App:Database:*");
    }

    [Fact]
    public void ToEnvironmentLabel_NullEnvironment_ReturnsNull()
    {
        var label = AzureKeyConventions.ToEnvironmentLabel(null, null);
        label.Should().BeNull();
    }

    [Fact]
    public void ToEnvironmentLabel_WithEnvironment_ReturnsLowercase()
    {
        var label = AzureKeyConventions.ToEnvironmentLabel("Staging", null);
        label.Should().Be("staging");
    }

    [Fact]
    public void ToEnvironmentLabel_WithTag_CombinesEnvironmentAndTag()
    {
        var label = AzureKeyConventions.ToEnvironmentLabel("Production", "canary");
        label.Should().Be("production-canary");
    }

    [Fact]
    public void ToTenantLabel_FormatsCorrectly()
    {
        var label = AzureKeyConventions.ToTenantLabel("acme-corp");
        label.Should().Be("tenant-acme-corp");
    }

    [Fact]
    public void ToKeyVaultName_ReplacesColonsWithDoubleDash()
    {
        var name = AzureKeyConventions.ToKeyVaultName("Pragmatic", "App:Database:ConnectionString");
        name.Should().Be("Pragmatic--App--Database--ConnectionString");
    }

    [Fact]
    public void ToKeyVaultTenantName_IncludesTenantSegment()
    {
        var name = AzureKeyConventions.ToKeyVaultTenantName("Pragmatic", "Api:Key", "tenant-A");
        name.Should().Be("Pragmatic--tenant--tenant-A--Api--Key");
    }
}
