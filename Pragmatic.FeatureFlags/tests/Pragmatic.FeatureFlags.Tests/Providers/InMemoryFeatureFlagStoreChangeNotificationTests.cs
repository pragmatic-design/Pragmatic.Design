using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Providers;

/// <summary>
///     Verifies <see cref="InMemoryFeatureFlagStore.Define" /> emits a change on ANY
///     definition mutation (rules, description, rollout percentage) — not only on an
///     <c>Enabled</c> flip — so watchers can react to rollout/rule edits.
/// </summary>
public class InMemoryFeatureFlagStoreChangeNotificationTests
{
    private static async Task<FeatureFlagChange?> FirstChangeOrTimeout(
        InMemoryFeatureFlagStore store, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var change in store.WatchAsync(cts.Token).ConfigureAwait(false))
                return change;
        }
        catch (OperationCanceledException)
        {
            // No change observed within the window.
        }

        return null;
    }

    [Fact]
    public async Task Define_NewFlag_DoesNotEmitChange()
    {
        var store = new InMemoryFeatureFlagStore();

        store.Define(new FeatureFlagDefinition { Name = "brand-new", Enabled = true });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromMilliseconds(150));
        change.Should().BeNull("defining a flag for the first time is not a change to an existing flag");
    }

    [Fact]
    public async Task Define_DescriptionChange_EmitsChange()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = true, Description = "v1" });

        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = true, Description = "v2" });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromSeconds(2));
        change.Should().NotBeNull();
        change!.FlagName.Should().Be("flag");
    }

    [Fact]
    public async Task Define_RuleCountChange_EmitsChange()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition { Name = "flag", Enabled = true });

        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "tenant", Values = ["acme"], Enabled = true }]
        });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromSeconds(2));
        change.Should().NotBeNull("adding a rule is a mutation watchers should observe");
    }

    [Fact]
    public async Task Define_RolloutPercentageChange_EmitsChange()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["10"], Enabled = true }]
        });

        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["10", "extra"], Enabled = true }]
        });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromSeconds(2));
        change.Should().NotBeNull("changing a rule's value count is a mutation watchers should observe");
    }

    [Fact]
    public async Task Define_RolloutPercentageValueEdit_SameCount_EmitsChange()
    {
        // a rollout change from ["10"] to ["20"] keeps the same value count;
        // the store must still notify watchers of the edit.
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["10"], Enabled = true }]
        });

        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["20"], Enabled = true }]
        });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromSeconds(2));
        change.Should().NotBeNull("editing a rule value with the same count is a mutation watchers should observe");
    }

    [Fact]
    public async Task Define_TenantValueSwap_SameCount_EmitsChange()
    {
        // replacing ["tenant-a"] with ["tenant-b"] keeps the same value count.
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "tenant", Values = ["tenant-a"], Enabled = true }]
        });

        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "tenant", Values = ["tenant-b"], Enabled = true }]
        });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromSeconds(2));
        change.Should().NotBeNull("swapping a tenant id is a mutation watchers should observe");
    }

    [Fact]
    public async Task Define_IdenticalDefinition_DoesNotEmitChange()
    {
        // Guard against false positives: redefining an identical flag must not notify.
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Description = "v1",
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["10"], Enabled = true }]
        });

        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Description = "v1",
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["10"], Enabled = true }]
        });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromMilliseconds(150));
        change.Should().BeNull("redefining an identical flag is not a change");
    }

    [Fact]
    public async Task Define_RuleEnabledFlagChange_EmitsChange()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "tenant", Values = ["acme"], Enabled = true }]
        });

        store.Define(new FeatureFlagDefinition
        {
            Name = "flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "tenant", Values = ["acme"], Enabled = false }]
        });

        var change = await FirstChangeOrTimeout(store, TimeSpan.FromSeconds(2));
        change.Should().NotBeNull("flipping a rule's Enabled flag is a mutation watchers should observe");
    }
}
