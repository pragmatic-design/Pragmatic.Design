using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.Tests.Lifecycle;

public sealed class LifecycleContextTests
{
    // Now_Default_IsCloseToUtcNow was here, and it pinned the defect: it asserted that a context
    // built without an instant reads the wall clock, which is exactly what made a forgotten Now
    // invisible. Now is required, so there is no default left to assert; what replaced the assertion
    // is the compiler, plus ANowThatMustBeGivenTests for the factories that absorb it.

    [Fact]
    public void Now_CanBeOverridden()
    {
        var fixedNow = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var ctx = new LifecycleContext { Now = fixedNow };

        ctx.Now.Should().Be(fixedNow);
    }

    [Fact]
    public void UserId_Default_IsNull()
    {
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch);
        ctx.UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_CanBeSet()
    {
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch) with { UserId = "user-42" };
        ctx.UserId.Should().Be("user-42");
    }

    [Fact]
    public void TenantId_Default_IsNull()
    {
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch);
        ctx.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantId_CanBeSet()
    {
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch) with { TenantId = "tenant-1" };
        ctx.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void Metadata_Default_IsNull()
    {
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch);
        ctx.Metadata.Should().BeNull();
    }

    [Fact]
    public void Metadata_CanBeSet()
    {
        var metadata = new Dictionary<string, object> { ["key"] = "value" };
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch) with { Metadata = metadata };

        ctx.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value");
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var ctx1 = new LifecycleContext { Now = now, UserId = "u1", TenantId = "t1" };
        var ctx2 = new LifecycleContext { Now = now, UserId = "u1", TenantId = "t1" };

        ctx1.Should().Be(ctx2);
    }

    [Fact]
    public void Record_WithExpression_CreatesModifiedCopy()
    {
        var ctx = LifecycleContext.At(DateTimeOffset.UnixEpoch) with { UserId = "u1", TenantId = "t1" };
        var modified = ctx with { UserId = "u2" };

        modified.UserId.Should().Be("u2");
        modified.TenantId.Should().Be("t1");
        ctx.UserId.Should().Be("u1");
    }
}
