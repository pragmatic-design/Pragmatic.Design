using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Edge-case coverage for <see cref="TenantScope"/> beyond the happy-path
///     scenarios in <see cref="TenantScopeTests"/>. Each test always disposes the
///     scope it opens so the ambient <c>AsyncLocal</c> state never leaks across tests.
/// </summary>
[Collection(TenantScopeCollection.Name)]
public class TenantScopeEdgeCaseTests
{
    [Fact]
    public void BeginScope_WithoutName_LeavesTenantNameNull()
    {
        var scope = new TenantScope();

        using var _ = TenantScope.BeginScope("tenant-only");

        scope.TenantId.Should().Be("tenant-only");
        scope.TenantName.Should().BeNull();
        scope.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void BeginScope_WithEmptyTenantId_IsStillResolved()
    {
        // IsResolved reflects whether a scope was begun (state present), not whether the id is non-empty.
        var scope = new TenantScope();

        using var _ = TenantScope.BeginScope("");

        scope.TenantId.Should().BeEmpty();
        scope.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void BeginScope_ReturnsDisposable()
    {
        var handle = TenantScope.BeginScope("disposable-tenant");

        handle.Should().BeAssignableTo<IDisposable>();

        handle.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_RemainsRestored()
    {
        var scope = new TenantScope();
        var handle = TenantScope.BeginScope("twice");

        handle.Dispose();
        handle.Dispose();

        scope.TenantId.Should().BeNull();
        scope.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void NestedScopes_ThreeDeep_RestoreEachLevel()
    {
        var scope = new TenantScope();

        using (TenantScope.BeginScope("a"))
        {
            using (TenantScope.BeginScope("b"))
            {
                using (TenantScope.BeginScope("c"))
                {
                    scope.TenantId.Should().Be("c");
                }

                scope.TenantId.Should().Be("b");
            }

            scope.TenantId.Should().Be("a");
        }

        scope.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void SeparateInstances_ShareAmbientState()
    {
        // TenantScope state is static AsyncLocal: all instances observe the same ambient scope.
        var first = new TenantScope();
        var second = new TenantScope();

        using var _ = TenantScope.BeginScope("shared");

        first.TenantId.Should().Be("shared");
        second.TenantId.Should().Be("shared");
    }

    [Fact]
    public async Task ScopeBegunInChildTask_DoesNotLeakToParentFlow()
    {
        var scope = new TenantScope();

        await Task.Run(static () =>
        {
            using var _ = TenantScope.BeginScope("child-only");
        }).ConfigureAwait(true);

        // AsyncLocal mutations in a child flow do not propagate back up to the parent.
        scope.IsResolved.Should().BeFalse();
    }
}
