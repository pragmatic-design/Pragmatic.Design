using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Evaluation;

namespace Pragmatic.FeatureFlags.Tests.Evaluation;

public class FeatureFlagEvaluatorTests
{
    [Fact]
    public void Evaluate_NoRules_ReturnsGlobalState()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules = []
        };

        FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoRules_DisabledFlag_ReturnsFalse()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules = []
        };

        FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData("tenant-a", true)]
    [InlineData("tenant-b", true)]
    [InlineData("tenant-c", false)]
    public void Evaluate_TenantRule_MatchesByTenantId(string tenantId, bool expected)
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["tenant-a", "tenant-b"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { TenantId = tenantId };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_TenantRule_NullTenantId_FallsBackToGlobal()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["tenant-a"], Enabled = false }
            ]
        };

        var context = new FeatureFlagContext { TenantId = null };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Theory]
    [InlineData("user-1", true)]
    [InlineData("user-99", false)]
    public void Evaluate_UserRule_MatchesByUserId(string userId, bool expected)
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "user", Values = ["user-1", "user-2"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { UserId = userId };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().Be(expected);
    }

    [Theory]
    [InlineData("enterprise", true)]
    [InlineData("free", false)]
    public void Evaluate_PlanRule_MatchesByPlan(string plan, bool expected)
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "plan", Values = ["enterprise", "pro"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { Plan = plan };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().Be(expected);
    }

    [Theory]
    [InlineData("staging", true)]
    [InlineData("production", false)]
    public void Evaluate_EnvironmentRule_MatchesByEnvironment(string env, bool expected)
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "environment", Values = ["staging", "development"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { Environment = env };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_ListRule_IsCaseInsensitive()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["Tenant-A"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { TenantId = "tenant-a" };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PercentageRule_100Percent_AlwaysEnabled()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "percentage", Values = ["100"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { UserId = "any-user" };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PercentageRule_0Percent_AlwaysDisabled()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "percentage", Values = ["0"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { UserId = "any-user" };
        // 0% → false, no other rules → falls back to global (true)
        // Actually: 0% returns false directly
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_PercentageRule_IsDeterministic()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "percentage", Values = ["50"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { UserId = "user-123" };

        var first = FeatureFlagEvaluator.Evaluate(flag, context);
        var second = FeatureFlagEvaluator.Evaluate(flag, context);

        first.Should().Be(second, "same user+flag should always get same result");
    }

    [Fact]
    public void Evaluate_PercentageRule_InvalidValue_FallsBackToGlobal()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "percentage", Values = ["not-a-number"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { UserId = "user-123" };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PropertyRule_MatchesByKeyValue()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "property", Values = ["region", "eu-west", "eu-east"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext
        {
            Properties = new Dictionary<string, string> { ["region"] = "eu-west" }
        };

        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PropertyRule_NoMatch_FallsBackToGlobal()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "property", Values = ["region", "eu-west"], Enabled = false }
            ]
        };

        var context = new FeatureFlagContext
        {
            Properties = new Dictionary<string, string> { ["region"] = "us-east" }
        };

        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PropertyRule_MissingProperty_FallsBackToGlobal()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "property", Values = ["region", "eu-west"], Enabled = false }
            ]
        };

        FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PropertyRule_InsufficientValues_FallsBackToGlobal()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "property", Values = ["region"], Enabled = false }
            ]
        };

        FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_UnknownRuleType_IsSkipped()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = true,
            Rules =
            [
                new FeatureFlagRule { Type = "custom-unknown", Values = ["value"], Enabled = false }
            ]
        };

        FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FirstMatchWins_StopsAtFirstRule()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["tenant-a"], Enabled = true },
                new FeatureFlagRule { Type = "tenant", Values = ["tenant-a"], Enabled = false }
            ]
        };

        var context = new FeatureFlagContext { TenantId = "tenant-a" };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SkipsNonMatchingRules_UsesFirstMatch()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "feature-x",
            Enabled = false,
            Rules =
            [
                new FeatureFlagRule { Type = "tenant", Values = ["tenant-b"], Enabled = false },
                new FeatureFlagRule { Type = "user", Values = ["user-1"], Enabled = true }
            ]
        };

        var context = new FeatureFlagContext { TenantId = "tenant-a", UserId = "user-1" };
        FeatureFlagEvaluator.Evaluate(flag, context).Should().BeTrue();
    }
}
