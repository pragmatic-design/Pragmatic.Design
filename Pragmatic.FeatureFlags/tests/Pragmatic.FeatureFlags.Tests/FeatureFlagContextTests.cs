using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests;

/// <summary>
///     Covers the immutable <see cref="FeatureFlagContext" /> record contract: defaults,
///     the shared <see cref="FeatureFlagContext.Empty" /> singleton, and value semantics.
/// </summary>
public class FeatureFlagContextTests
{
    [Fact]
    public void Default_HasNoTargetingValues()
    {
        var context = new FeatureFlagContext();

        context.TenantId.Should().BeNull();
        context.UserId.Should().BeNull();
        context.Plan.Should().BeNull();
        context.Environment.Should().BeNull();
    }

    [Fact]
    public void Default_PropertiesAreEmptyNotNull()
    {
        var context = new FeatureFlagContext();

        context.Properties.Should().NotBeNull();
        context.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Empty_ReturnsSameSingletonInstance()
    {
        FeatureFlagContext.Empty.Should().BeSameAs(FeatureFlagContext.Empty);
    }

    [Fact]
    public void Empty_HasNoTargetingValues()
    {
        var empty = FeatureFlagContext.Empty;

        empty.TenantId.Should().BeNull();
        empty.UserId.Should().BeNull();
        empty.Plan.Should().BeNull();
        empty.Environment.Should().BeNull();
        empty.Properties.Should().BeEmpty();
    }

    [Fact]
    public void WithExpression_ProducesModifiedCopy_LeavingOriginalUnchanged()
    {
        var original = new FeatureFlagContext { TenantId = "acme" };

        var modified = original with { UserId = "user-1" };

        modified.TenantId.Should().Be("acme");
        modified.UserId.Should().Be("user-1");
        original.UserId.Should().BeNull("the original record must remain unchanged");
    }

    [Fact]
    public void ValueEquality_SameValues_AreEqual()
    {
        var a = new FeatureFlagContext { TenantId = "acme", Plan = "pro" };
        var b = new FeatureFlagContext { TenantId = "acme", Plan = "pro" };

        a.Should().Be(b);
    }

    [Fact]
    public void ValueEquality_DifferentValues_AreNotEqual()
    {
        var a = new FeatureFlagContext { TenantId = "acme" };
        var b = new FeatureFlagContext { TenantId = "globex" };

        a.Should().NotBe(b);
    }
}
