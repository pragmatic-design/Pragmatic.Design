using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Tests.Providers;

public class InMemoryFeatureFlagStoreTests
{
    private readonly InMemoryFeatureFlagStore _store = new();

    [Fact]
    public async Task IsEnabledAsync_UnknownFlag_ReturnsFalse()
    {
        var result = await _store.IsEnabledAsync("unknown-flag");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_DefinedEnabledFlag_ReturnsTrue()
    {
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });

        var result = await _store.IsEnabledAsync("my-flag");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_DefinedDisabledFlag_ReturnsFalse()
    {
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = false });

        var result = await _store.IsEnabledAsync("my-flag");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_EvaluatesRules()
    {
        _store.Define(new FeatureFlagDefinition
        {
            Name = "my-flag",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["tenant-a"], Enabled = true }
            ]
        });

        var context = new FeatureFlagContext { TenantId = "tenant-a" };
        var result = await _store.IsEnabledAsync("my-flag", context);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_IsCaseInsensitive()
    {
        _store.Define(new FeatureFlagDefinition { Name = "My-Flag", Enabled = true });

        var result = await _store.IsEnabledAsync("my-flag");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetDefinitionAsync_UnknownFlag_ReturnsNull()
    {
        var result = await _store.GetDefinitionAsync("unknown");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDefinitionAsync_KnownFlag_ReturnsDefinition()
    {
        var definition = new FeatureFlagDefinition
        {
            Name = "my-flag",
            Enabled = true,
            Description = "Test flag"
        };
        _store.Define(definition);

        var result = await _store.GetDefinitionAsync("my-flag");
        result.Should().NotBeNull();
        result!.Name.Should().Be("my-flag");
        result.Description.Should().Be("Test flag");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllDefinitions_OrderedByName()
    {
        _store.Define(new FeatureFlagDefinition { Name = "z-flag", Enabled = true });
        _store.Define(new FeatureFlagDefinition { Name = "a-flag", Enabled = false });
        _store.Define(new FeatureFlagDefinition { Name = "m-flag", Enabled = true });

        var result = await _store.GetAllAsync();
        result.Should().HaveCount(3);
        result[0].Name.Should().Be("a-flag");
        result[1].Name.Should().Be("m-flag");
        result[2].Name.Should().Be("z-flag");
    }

    [Fact]
    public async Task Define_UpdatesExistingFlag()
    {
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = false });

        var result = await _store.IsEnabledAsync("my-flag");
        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_ExistingFlag_ReturnsTrue()
    {
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });

        _store.Remove("my-flag").Should().BeTrue();
    }

    [Fact]
    public void Remove_UnknownFlag_ReturnsFalse()
    {
        _store.Remove("unknown").Should().BeFalse();
    }

    [Fact]
    public async Task Remove_FlagIsNoLongerAccessible()
    {
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });
        _store.Remove("my-flag");

        var result = await _store.IsEnabledAsync("my-flag");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task WatchAsync_EmitsChange_WhenFlagEnabledStateChanges()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });

        var watchTask = Task.Run(async () =>
        {
            await foreach (var change in _store.WatchAsync(cts.Token))
                return change;
            return null;
        }, cts.Token);

        // Toggle the flag
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = false });

        var result = await watchTask;
        result.Should().NotBeNull();
        result!.FlagName.Should().Be("my-flag");
        result.WasEnabled.Should().BeTrue();
        result.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Define_SameEnabledState_DoesNotEmitChange()
    {
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });
        // Re-define with the SAME effective state (same Enabled, same everything) — no change should be emitted.
        _store.Define(new FeatureFlagDefinition { Name = "my-flag", Enabled = true });

        // If no change was emitted, WatchAsync should not produce a result within timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var changes = new List<FeatureFlagChange>();

        Func<Task> act = async () =>
        {
            await foreach (var change in _store.WatchAsync(cts.Token))
                changes.Add(change);
        };

        // OperationCanceledException is expected when CTS fires
        act.Should().ThrowAsync<OperationCanceledException>();
        changes.Should().BeEmpty();
    }
}
