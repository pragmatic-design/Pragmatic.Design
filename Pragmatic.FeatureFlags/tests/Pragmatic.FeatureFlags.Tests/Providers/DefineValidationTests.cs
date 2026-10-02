using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Providers;

/// <summary>
///     A rule whose type the engine does not recognise never matches, so the flag behaves as if the rule were
///     absent — a typo in <c>"tenant"</c> would disable targeting with no signal at all. Defining one
///     programmatically therefore fails immediately.
/// </summary>
public class DefineValidationTests
{
    [Fact]
    public void Define_WithUnknownRuleType_Throws_AndNamesTheValidTypes()
    {
        var store = new InMemoryFeatureFlagStore();
        var definition = new FeatureFlagDefinition
        {
            Name = "beta",
            Enabled = false,
            Rules = [new FeatureFlagRule { Type = "tenat", Values = ["acme"] }]   // typo for "tenant"
        };

        var act = () => store.Define(definition);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*tenat*never match*")
            .WithMessage("*tenant*");
    }

    [Fact]
    public void Define_WithKnownRuleTypes_Succeeds_RegardlessOfCasing()
    {
        var store = new InMemoryFeatureFlagStore();
        var definition = new FeatureFlagDefinition
        {
            Name = "beta",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "Tenant", Values = ["acme"] },
                new FeatureFlagRule { Type = "PERCENTAGE", Values = ["20"] }
            ]
        };

        var act = () => store.Define(definition);

        act.Should().NotThrow("rule types match case-insensitively, exactly as the evaluator compares them");
    }

    [Fact]
    public void Define_WithFactoryBuiltRules_Succeeds()
    {
        var store = new InMemoryFeatureFlagStore();
        var definition = new FeatureFlagDefinition
        {
            Name = "beta",
            Enabled = false,
            Rules =
            [
                FeatureFlagRule.Tenant("acme").Denying(),
                FeatureFlagRule.User("u1"),
                FeatureFlagRule.Plan("pro"),
                FeatureFlagRule.Property("region", "eu"),
                FeatureFlagRule.Percentage(20)
            ]
        };

        var act = () => store.Define(definition);

        act.Should().NotThrow();
    }

    [Fact]
    public void KnownTypes_CoversEveryTypeTheEvaluatorHandles()
    {
        // Guards against a rule type being added to the evaluator without being added here — which would
        // make Define reject a rule that actually works.
        FeatureFlagRule.KnownTypes.Should().BeEquivalentTo(
            ["tenant", "user", "plan", "environment", "percentage", "property"]);
    }

    [Fact]
    public void IsKnownType_IsCaseInsensitive_AndRejectsNull()
    {
        FeatureFlagRule.IsKnownType("TENANT").Should().BeTrue();
        FeatureFlagRule.IsKnownType("nope").Should().BeFalse();
        FeatureFlagRule.IsKnownType(null).Should().BeFalse();
    }
}
