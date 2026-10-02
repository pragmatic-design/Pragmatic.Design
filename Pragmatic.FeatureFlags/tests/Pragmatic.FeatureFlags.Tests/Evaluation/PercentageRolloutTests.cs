using Pragmatic.Testing.Assertions;
using Pragmatic.FeatureFlags.Evaluation;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests.Evaluation;

/// <summary>
///     Deterministic percentage-rollout behaviour. The evaluator buckets each
///     (flag name + seed) pair with a stable SHA-256 hash, so results are reproducible
///     across instances and process runs — no RNG, no ambient state, no culture reliance.
/// </summary>
public class PercentageRolloutTests
{
    private static FeatureFlagDefinition Percentage(int percent, bool ruleEnabled = true, bool globalEnabled = false) =>
        new()
        {
            Name = "rollout-flag",
            Enabled = globalEnabled,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = [percent.ToString()], Enabled = ruleEnabled }]
        };

    [Fact]
    public void Evaluate_ZeroPercent_ReturnsFalse_EvenWhenGlobalEnabled()
    {
        var flag = Percentage(0, globalEnabled: true);

        FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = "user-1" }).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NegativePercent_ReturnsFalse()
    {
        var flag = Percentage(-25, globalEnabled: true);

        FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = "user-1" }).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_HundredPercent_ReturnsTrue_EvenWhenRuleEnabledFalseAndGlobalFalse()
    {
        var flag = Percentage(100, ruleEnabled: false, globalEnabled: false);

        FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = "any-user" }).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_OverHundredPercent_ReturnsTrue()
    {
        var flag = Percentage(150, ruleEnabled: false);

        FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = "any-user" }).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FiftyPercent_IsStableAcrossSeparateEvaluations()
    {
        var flag = Percentage(50);
        var context = new FeatureFlagContext { UserId = "user-stable" };

        var results = Enumerable.Range(0, 20)
            .Select(_ => FeatureFlagEvaluator.Evaluate(flag, context))
            .Distinct()
            .ToList();

        results.Should().ContainSingle("the same flag+seed must always bucket identically");
    }

    [Fact]
    public void Evaluate_BucketingDependsOnUserId_NotAllUsersBucketTheSame()
    {
        var flag = Percentage(50);

        var enabledForSome = false;
        var disabledForSome = false;
        for (var i = 0; i < 200; i++)
        {
            var enabled = FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = $"user-{i}" });
            enabledForSome |= enabled;
            disabledForSome |= !enabled;
        }

        enabledForSome.Should().BeTrue("a 50% rollout must enable at least some users");
        disabledForSome.Should().BeTrue("a 50% rollout must disable at least some users");
    }

    [Fact]
    public void Evaluate_FiftyPercent_DistributionIsRoughlyHalf()
    {
        var flag = Percentage(50);

        var enabledCount = Enumerable.Range(0, 1000)
            .Count(i => FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = $"user-{i}" }));

        // SHA-256 buckets uniformly; over 1000 deterministic keys ~50% should land in-bucket.
        // Wide tolerance keeps the assertion robust while still proving real hash-bucketing
        // (not a constant). This is fully deterministic — same keys, same hash, every run.
        enabledCount.Should().BeInRange(400, 600);
    }

    [Fact]
    public void Evaluate_TenPercent_FewerEnabledThanNinetyPercent()
    {
        var ten = Percentage(10);
        var ninety = Percentage(90);

        var tenCount = Enumerable.Range(0, 1000)
            .Count(i => FeatureFlagEvaluator.Evaluate(ten, new FeatureFlagContext { UserId = $"user-{i}" }));
        var ninetyCount = Enumerable.Range(0, 1000)
            .Count(i => FeatureFlagEvaluator.Evaluate(ninety, new FeatureFlagContext { UserId = $"user-{i}" }));

        tenCount.Should().BeLessThan(ninetyCount, "a higher rollout percentage must enable more users");
    }

    [Fact]
    public void Evaluate_FallsBackToTenantId_WhenUserIdMissing()
    {
        var flag = Percentage(50);
        var byTenant = new FeatureFlagContext { TenantId = "tenant-xyz" };

        // Without a UserId the evaluator seeds on TenantId; the result must still be stable.
        var first = FeatureFlagEvaluator.Evaluate(flag, byTenant);
        var second = FeatureFlagEvaluator.Evaluate(flag, byTenant);

        first.Should().Be(second);
    }

    [Fact]
    public void Evaluate_AnonymousSeed_IsStable_WhenNoUserOrTenant()
    {
        var flag = Percentage(50);

        var first = FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty);
        var second = FeatureFlagEvaluator.Evaluate(flag, FeatureFlagContext.Empty);

        first.Should().Be(second, "the 'anonymous' fallback seed must produce a deterministic bucket");
    }

    [Fact]
    public void Evaluate_EmptyValues_FallsBackToGlobalState()
    {
        var flag = new FeatureFlagDefinition
        {
            Name = "rollout-flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = [], Enabled = true }]
        };

        FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = "user-1" }).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_InBucketWithRuleDisabled_ReturnsFalse()
    {
        // For a user that lands in-bucket at 100%-but-not-saturated boundary we cannot assume
        // membership, so use a high percentage and a rule that disables on match. A user that
        // matches the bucket should get rule.Enabled (false); one that misses falls back to global.
        var flag = new FeatureFlagDefinition
        {
            Name = "rollout-flag",
            Enabled = true,
            Rules = [new FeatureFlagRule { Type = "percentage", Values = ["99"], Enabled = false }]
        };

        var anyFalse = false;
        for (var i = 0; i < 200; i++)
        {
            if (!FeatureFlagEvaluator.Evaluate(flag, new FeatureFlagContext { UserId = $"user-{i}" }))
            {
                anyFalse = true;
                break;
            }
        }

        anyFalse.Should().BeTrue("an in-bucket match with rule.Enabled=false must yield false for some user");
    }
}
