using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Pragmatic.FeatureFlags.Configuration;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Providers;

/// <summary>
///     Reload-driven change emission for <see cref="ConfigurationFeatureFlagStore" />, including
///     the removal branch (a previously-enabled flag that disappears emits a disable change) and
///     the post-reload read path (the cache is swapped, so reads reflect new config).
/// </summary>
public class ConfigurationFeatureFlagStoreReloadTests
{
    [Fact]
    public async Task Reload_RemovingEnabledFlag_EmitsDisableChange()
    {
        var source = new ReloadableSource(new Dictionary<string, string?>
        {
            ["FeatureFlags:Beta:Enabled"] = "true"
        });
        var config = new ConfigurationBuilder().Add(source).Build();
        using var store = new ConfigurationFeatureFlagStore(config);

        var collector = Task.Run(async () =>
        {
            await foreach (var change in store.WatchAsync().ConfigureAwait(false))
                return change;
            return null;
        });

        await Task.Delay(50);

        // The flag disappears entirely from configuration.
        source.Replace(new Dictionary<string, string?>());

        var observed = await collector.WaitAsync(TimeSpan.FromSeconds(2));

        observed.Should().NotBeNull();
        observed!.FlagName.Should().Be("Beta");
        observed.WasEnabled.Should().BeTrue();
        observed.IsEnabled.Should().BeFalse("a removed, previously-enabled flag is reported as disabled");
    }

    [Fact]
    public async Task Reload_AfterChange_ReadsReflectNewConfiguration()
    {
        var source = new ReloadableSource(new Dictionary<string, string?>
        {
            ["FeatureFlags:Beta:Enabled"] = "false"
        });
        var config = new ConfigurationBuilder().Add(source).Build();
        using var store = new ConfigurationFeatureFlagStore(config);

        (await store.IsEnabledAsync("Beta")).Should().BeFalse();

        source.Replace(new Dictionary<string, string?>
        {
            ["FeatureFlags:Beta:Enabled"] = "true"
        });

        // ReloadFlags swaps the cache synchronously inside OnReload, so the next read sees it.
        (await store.IsEnabledAsync("Beta")).Should().BeTrue();
    }

    [Fact]
    public async Task Reload_DisabledFlagRemoved_DoesNotEmitChange()
    {
        var source = new ReloadableSource(new Dictionary<string, string?>
        {
            ["FeatureFlags:Beta:Enabled"] = "false"
        });
        var config = new ConfigurationBuilder().Add(source).Build();
        using var store = new ConfigurationFeatureFlagStore(config);

        FeatureFlagChange? observed = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var collector = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in store.WatchAsync(cts.Token).ConfigureAwait(false))
                {
                    observed = change;
                    return;
                }
            }
            catch (OperationCanceledException) { }
        }, cts.Token);

        await Task.Delay(50);
        // Removing an already-disabled flag is a no-op transition (false -> absent/false).
        source.Replace(new Dictionary<string, string?>());

        await collector;
        observed.Should().BeNull("removing a flag that was already disabled is not a state transition");
    }

    private sealed class ReloadableSource(Dictionary<string, string?> initial) : IConfigurationSource
    {
        private readonly ReloadableProvider _provider = new(initial);

        public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;

        public void Replace(Dictionary<string, string?> data) => _provider.Replace(data);
    }

    private sealed class ReloadableProvider(Dictionary<string, string?> initial) : ConfigurationProvider
    {
        public override void Load() =>
            Data = new Dictionary<string, string?>(initial, StringComparer.OrdinalIgnoreCase);

        public void Replace(Dictionary<string, string?> data)
        {
            Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }
    }
}
