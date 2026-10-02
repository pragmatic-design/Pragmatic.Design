using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests;

/// <summary>
///     Covers the <see cref="FeatureFlagStoreExtensions" /> strongly-typed overloads
///     (<c>IsEnabledAsync&lt;TFlag&gt;</c> / <c>GetDefinitionAsync&lt;TFlag&gt;</c>), which
///     resolve the flag key from the static <see cref="IFeatureFlag.Name" /> member.
/// </summary>
public class StronglyTypedFeatureFlagExtensionsTests
{
    private sealed class LoyaltyDiscountFlag : IFeatureFlag
    {
        public static string Name => "loyalty-discount";
        public static string? Description => "10% loyalty discount — gradual rollout";
    }

    [Fact]
    public async Task IsEnabledAsyncOfTFlag_UsesStaticName_ReturnsEnabledState()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition { Name = LoyaltyDiscountFlag.Name, Enabled = true });

        var result = await store.IsEnabledAsync<LoyaltyDiscountFlag>();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsyncOfTFlag_UndefinedFlag_ReturnsFalse()
    {
        var store = new InMemoryFeatureFlagStore();

        var result = await store.IsEnabledAsync<LoyaltyDiscountFlag>();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsyncOfTFlag_WithContext_EvaluatesRulesAgainstStaticName()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = LoyaltyDiscountFlag.Name,
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["acme"], Enabled = true }
            ]
        });

        var matched = await store.IsEnabledAsync<LoyaltyDiscountFlag>(new FeatureFlagContext { TenantId = "acme" });
        var unmatched = await store.IsEnabledAsync<LoyaltyDiscountFlag>(new FeatureFlagContext { TenantId = "other" });

        matched.Should().BeTrue();
        unmatched.Should().BeFalse();
    }

    [Fact]
    public async Task GetDefinitionAsyncOfTFlag_ReturnsDefinitionForStaticName()
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(new FeatureFlagDefinition
        {
            Name = LoyaltyDiscountFlag.Name,
            Enabled = true,
            Description = "Loyalty discount rollout"
        });

        var definition = await store.GetDefinitionAsync<LoyaltyDiscountFlag>();

        definition.Should().NotBeNull();
        definition!.Name.Should().Be("loyalty-discount");
        definition.Description.Should().Be("Loyalty discount rollout");
    }

    [Fact]
    public async Task GetDefinitionAsyncOfTFlag_UndefinedFlag_ReturnsNull()
    {
        var store = new InMemoryFeatureFlagStore();

        var definition = await store.GetDefinitionAsync<LoyaltyDiscountFlag>();

        definition.Should().BeNull();
    }
}
