using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Pragmatic.Configuration.Bridge;
using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Tests.Unit;

public class PragmaticConfigurationSourceTests
{
    [Fact]
    public async Task ConfigurationBuilder_WithPragmaticStore_ExposesValues()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("App:ConnectionString", "Server=localhost");
        await store.SetAsync("App:MaxRetries", "3");

        var env = EnvironmentProfile.From("Production");

        var config = new ConfigurationBuilder()
            .AddPragmaticStore(store, env)
            .Build();

        config["App:ConnectionString"].Should().Be("Server=localhost");
        config["App:MaxRetries"].Should().Be("3");
    }

    [Fact]
    public async Task ConfigurationBuilder_WithPrefix_FiltersKeys()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("App:Setting", "value1");
        await store.SetAsync("Other:Setting", "value2");

        var env = EnvironmentProfile.From("Production");

        var config = new ConfigurationBuilder()
            .AddPragmaticStore(store, env, keyPrefix: "App:")
            .Build();

        config["App:Setting"].Should().Be("value1");
        config["Other:Setting"].Should().BeNull();
    }

    [Fact]
    public async Task ConfigurationBuilder_PragmaticOverridesJsonDefaults()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("App:Timeout", "999");

        var env = EnvironmentProfile.From("Production");

        // Pragmatic source added AFTER in-memory — last wins
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:Timeout"] = "30"
            })
            .AddPragmaticStore(store, env)
            .Build();

        config["App:Timeout"].Should().Be("999");
    }

    [Fact]
    public async Task ConfigurationBuilder_EnvironmentOverlay_Works()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("App:LogLevel", "Warning");
        await store.SetAsync("staging/App:LogLevel", "Debug");

        var env = EnvironmentProfile.From("Staging");

        var config = new ConfigurationBuilder()
            .AddPragmaticStore(store, env)
            .Build();

        config["App:LogLevel"].Should().Be("Debug");
    }
}
