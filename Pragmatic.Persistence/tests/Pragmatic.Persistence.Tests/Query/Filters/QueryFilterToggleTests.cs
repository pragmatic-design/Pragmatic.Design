using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Tests.Query.Filters;

public class QueryFilterToggleTests
{
    [Fact]
    public void IsDisabled_NoScope_ReturnsFalse()
    {
        var toggle = new QueryFilterToggle();

        toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeFalse();
        toggle.AllDisabled.Should().BeFalse();
    }

    [Fact]
    public void Disable_SpecificFilter_IsDisabledReturnsTrue()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.Disable<TestSoftDeleteFilter>())
        {
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            toggle.IsDisabled<TestTenantFilter>().Should().BeFalse();
        }
    }

    [Fact]
    public void Disable_ByType_IsDisabledReturnsTrue()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.Disable(typeof(TestSoftDeleteFilter)))
        {
            toggle.IsDisabled(typeof(TestSoftDeleteFilter)).Should().BeTrue();
            toggle.IsDisabled(typeof(TestTenantFilter)).Should().BeFalse();
        }
    }

    [Fact]
    public void Disable_AfterDispose_ReEnablesFilter()
    {
        var toggle = new QueryFilterToggle();

        var scope = toggle.Disable<TestSoftDeleteFilter>();
        toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();

        scope.Dispose();
        toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeFalse();
    }

    [Fact]
    public void Disable_DoubleDispose_DoesNotThrow()
    {
        var toggle = new QueryFilterToggle();

        var scope = toggle.Disable<TestSoftDeleteFilter>();
        scope.Dispose();

        var act = () => scope.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void DisableAll_DisablesEveryFilter()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.DisableAll())
        {
            toggle.AllDisabled.Should().BeTrue();
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            toggle.IsDisabled<TestTenantFilter>().Should().BeTrue();
        }

        toggle.AllDisabled.Should().BeFalse();
    }

    [Fact]
    public void NestedScopes_IndependentLifetimes()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.Disable<TestSoftDeleteFilter>())
        {
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            toggle.IsDisabled<TestTenantFilter>().Should().BeFalse();

            using (toggle.Disable<TestTenantFilter>())
            {
                toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
                toggle.IsDisabled<TestTenantFilter>().Should().BeTrue();
            }

            // Tenant re-enabled, SoftDelete still disabled
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            toggle.IsDisabled<TestTenantFilter>().Should().BeFalse();
        }

        toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeFalse();
    }

    [Fact]
    public void DisableAll_NestedWithSpecific_BothWork()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.DisableAll())
        {
            using (toggle.Disable<TestSoftDeleteFilter>())
            {
                toggle.AllDisabled.Should().BeTrue();
                toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            }

            // DisableAll still active
            toggle.AllDisabled.Should().BeTrue();
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
        }

        toggle.AllDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task AsyncLocal_IndependentPerAsyncFlow()
    {
        var toggle = new QueryFilterToggle();
        var barrier = new TaskCompletionSource();

        var task1 = Task.Run(async () =>
        {
            using (toggle.Disable<TestSoftDeleteFilter>())
            {
                barrier.SetResult();
                await Task.Delay(50);
                return toggle.IsDisabled<TestSoftDeleteFilter>();
            }
        });

        await barrier.Task;

        // Different async flow — should NOT see the disable
        var isDisabledInMainFlow = toggle.IsDisabled<TestSoftDeleteFilter>();

        var wasDisabledInTask = await task1;

        wasDisabledInTask.Should().BeTrue();
        isDisabledInMainFlow.Should().BeFalse();
    }

    [Fact]
    public void CurrentMode_Default_IsNormal()
    {
        var toggle = new QueryFilterToggle();

        toggle.CurrentMode.Should().Be(FilterMode.Normal);
    }

    [Fact]
    public void UseMode_SetsCurrentMode()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.UseMode(FilterMode.Admin))
        {
            toggle.CurrentMode.Should().Be(FilterMode.Admin);
        }

        toggle.CurrentMode.Should().Be(FilterMode.Normal);
    }

    [Fact]
    public void UseMode_NestedScopes_RestoresPreviousMode()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.UseMode(FilterMode.Admin))
        {
            toggle.CurrentMode.Should().Be(FilterMode.Admin);

            using (toggle.UseMode(FilterMode.Raw))
            {
                toggle.CurrentMode.Should().Be(FilterMode.Raw);
            }

            toggle.CurrentMode.Should().Be(FilterMode.Admin);
        }

        toggle.CurrentMode.Should().Be(FilterMode.Normal);
    }

    [Fact]
    public void UseMode_Background_CanCombineWithDisable()
    {
        var toggle = new QueryFilterToggle();

        using (toggle.UseMode(FilterMode.Background))
        {
            using (toggle.Disable<TestSoftDeleteFilter>())
            {
                toggle.CurrentMode.Should().Be(FilterMode.Background);
                toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Disable_SameFilterNestedTwice_InnerDisposeKeepsOuterDisabled()
    {
        // A HashSet add/remove would collapse nested same-filter disables — disposing the inner
        // scope would remove the single entry and re-enable the filter while the outer scope is
        // still active. Copy-on-write snapshots restore the correct prior state.
        var toggle = new QueryFilterToggle();

        using (toggle.Disable<TestSoftDeleteFilter>())
        {
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();

            using (toggle.Disable<TestSoftDeleteFilter>())
            {
                toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
            }

            // Inner disposed, OUTER still active → must remain disabled.
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
        }

        toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeFalse();
    }

    [Fact]
    public async Task Disable_ParentStateBeforeChildFlow_ChildDoesNotMutateParent()
    {
        // If AsyncLocal held a MUTABLE object, a child flow inheriting a non-null parent state would
        // mutate the parent's set. With copy-on-write each write reassigns the per-flow slot.
        var toggle = new QueryFilterToggle();

        using (toggle.Disable<TestSoftDeleteFilter>())
        {
            await Task.Run(() =>
            {
                // Child inherits SoftDelete-disabled; add Tenant only in the child flow.
                using (toggle.Disable<TestTenantFilter>())
                {
                    toggle.IsDisabled<TestTenantFilter>().Should().BeTrue();
                }
            });

            // The child's Tenant disable must NOT have leaked into the parent flow.
            toggle.IsDisabled<TestTenantFilter>().Should().BeFalse();
            toggle.IsDisabled<TestSoftDeleteFilter>().Should().BeTrue();
        }
    }

    // Test filter stubs
    private sealed class TestSoftDeleteFilter : IQueryFilter { }
    private sealed class TestTenantFilter : IQueryFilter { }
}
