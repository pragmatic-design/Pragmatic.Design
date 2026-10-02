using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Configuration.Providers;
using Xunit;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>Verifies the required-keys manifest: startup fails fast when a declared key does not resolve.</summary>
public class RequiredConfigurationTests
{
    private static async Task<ServiceProvider> BuildAsync(string[] required, params (string Key, string Value)[] seed)
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration();
        services.AddRequiredConfiguration(required);

        var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IConfigurationStore>();
        foreach (var (key, value) in seed)
            await store.SetAsync(key, value);
        return sp;
    }

    private static Task StartAsync(ServiceProvider sp)
    {
        var hosted = sp.GetServices<IHostedService>();
        return Task.WhenAll(hosted.Select(h => h.StartAsync(default)));
    }

    [Fact]
    public async Task Startup_MissingRequiredKey_Throws_ListingIt()
    {
        var sp = await BuildAsync(["Db:ConnectionString", "Api:Key"], ("Db:ConnectionString", "value"));
        await using (sp.ConfigureAwait(false))
        {
            var act = () => StartAsync(sp);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("Api:Key").And.NotContain("Db:ConnectionString");
        }
    }

    [Fact]
    public async Task Startup_AllRequiredKeysPresent_DoesNotThrow()
    {
        var sp = await BuildAsync(
            ["Db:ConnectionString", "Api:Key"],
            ("Db:ConnectionString", "value"), ("Api:Key", "k"));
        await using (sp.ConfigureAwait(false))
        {
            var act = () => StartAsync(sp);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task AddRequiredConfiguration_Accumulates_AcrossCalls()
    {
        var services = new ServiceCollection();
        services.AddPragmaticConfiguration();
        services.AddRequiredConfiguration("A");
        services.AddRequiredConfiguration("B"); // second call must not replace the first

        var sp = services.BuildServiceProvider();
        await using (sp.ConfigureAwait(false))
        {
            await sp.GetRequiredService<IConfigurationStore>().SetAsync("A", "x"); // B still missing

            var act = () => StartAsync(sp);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("B");
        }
    }
}
