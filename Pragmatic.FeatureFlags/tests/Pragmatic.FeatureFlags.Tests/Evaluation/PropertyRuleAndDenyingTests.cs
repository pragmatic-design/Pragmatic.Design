using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Evaluation;

/// <summary>
///     Covers two rough edges of rule evaluation: property keys had to match case-exactly while their
///     values matched case-insensitively, and only <c>Percentage</c> could express a denial — leaving
///     "off for this tenant" reachable solely by hand-writing the rule type as a magic string.
/// </summary>
public class PropertyRuleAndDenyingTests
{
    private static async Task<bool> EvaluateAsync(FeatureFlagDefinition definition, FeatureFlagContext context)
    {
        var store = new InMemoryFeatureFlagStore();
        store.Define(definition);
        return await store.IsEnabledAsync(definition.Name, context);
    }

    [Fact]
    public async Task PropertyRule_MatchesKeyCaseInsensitively()
    {
        var definition = new FeatureFlagDefinition
        {
            Name = "beta",
            Enabled = false,
            Rules = [FeatureFlagRule.Property("Region", "eu")]
        };

        var context = new FeatureFlagContext
        {
            // A plain Dictionary uses an ordinal comparer, so the key casing differs from the rule's.
            Properties = new Dictionary<string, string> { ["region"] = "EU" }
        };

        (await EvaluateAsync(definition, context)).Should().BeTrue(
            "the key must match case-insensitively, exactly as the value already did");
    }

    [Fact]
    public async Task PropertyRule_WithUnknownKey_DoesNotMatch()
    {
        var definition = new FeatureFlagDefinition
        {
            Name = "beta",
            Enabled = false,
            Rules = [FeatureFlagRule.Property("Region", "eu")]
        };

        var context = new FeatureFlagContext
        {
            Properties = new Dictionary<string, string> { ["tier"] = "gold" }
        };

        (await EvaluateAsync(definition, context)).Should().BeFalse();
    }

    [Fact]
    public async Task DenyingRule_TurnsAMatchIntoADenial()
    {
        var definition = new FeatureFlagDefinition
        {
            Name = "new-checkout",
            Enabled = true,
            Rules = [FeatureFlagRule.Tenant("blocked-tenant").Denying()]
        };

        (await EvaluateAsync(definition, new FeatureFlagContext { TenantId = "blocked-tenant" }))
            .Should().BeFalse("the denying rule matched first and wins");

        (await EvaluateAsync(definition, new FeatureFlagContext { TenantId = "other-tenant" }))
            .Should().BeTrue("no rule matched, so the global Enabled state applies");
    }

    [Fact]
    public void Denying_LeavesTheOriginalRuleUntouched()
    {
        var allow = FeatureFlagRule.Tenant("acme");

        var deny = allow.Denying();

        allow.Enabled.Should().BeTrue("the record must not be mutated in place");
        deny.Enabled.Should().BeFalse();
        deny.Values.Should().Equal(allow.Values);
        deny.Type.Should().Be(allow.Type);
    }
}
