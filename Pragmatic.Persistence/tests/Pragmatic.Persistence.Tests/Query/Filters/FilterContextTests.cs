using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Tests.Query.Filters;

public class FilterContextTests
{
    [Fact]
    public void Default_IsNormalMode()
    {
        var ctx = FilterContext.At(DateTimeOffset.UnixEpoch);

        ctx.Mode.Should().Be(FilterMode.Normal);
        ctx.IsRaw.Should().BeFalse();
        ctx.SkipVisibility.Should().BeFalse();
        ctx.SkipPermissionBased.Should().BeFalse();
        ctx.SkipTenant.Should().BeFalse();
    }

    [Fact]
    public void AdminMode_SkipsVisibilityAndPermission()
    {
        var ctx = FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = FilterMode.Admin };

        ctx.IsRaw.Should().BeFalse();
        ctx.SkipVisibility.Should().BeTrue();
        ctx.SkipPermissionBased.Should().BeTrue();
        ctx.SkipTenant.Should().BeFalse();
    }

    [Fact]
    public void BackgroundMode_SkipsVisibilityPermissionAndTenant()
    {
        var ctx = FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = FilterMode.Background };

        ctx.IsRaw.Should().BeFalse();
        ctx.SkipVisibility.Should().BeTrue();
        ctx.SkipPermissionBased.Should().BeTrue();
        ctx.SkipTenant.Should().BeTrue();
    }

    [Fact]
    public void RawMode_SkipsEverything()
    {
        var ctx = FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = FilterMode.Raw };

        ctx.IsRaw.Should().BeTrue();
        ctx.SkipVisibility.Should().BeTrue();
        ctx.SkipPermissionBased.Should().BeTrue();
        ctx.SkipTenant.Should().BeTrue();
    }

    // Now_DefaultsToUtcNow was here, and its name said the defect out loud: the property that exists
    // so a caller can pin the time defaulted to the clock it exists to replace. Now is required, so
    // there is no default to assert; ANowThatMustBeGivenTests asserts the factories instead.

    [Fact]
    public void DisabledFilters_DefaultsToEmpty()
    {
        var ctx = FilterContext.At(DateTimeOffset.UnixEpoch);

        ctx.DisabledFilters.Should().BeEmpty();
    }

    [Fact]
    public void DisabledFilters_CanBeSet()
    {
        var disabled = new HashSet<Type> { typeof(object) };
        var ctx = FilterContext.At(DateTimeOffset.UnixEpoch) with { DisabledFilters = disabled };

        ctx.DisabledFilters.Should().Contain(typeof(object));
    }
}
