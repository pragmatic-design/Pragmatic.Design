using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Providers;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

public class CompositePermissionProviderTests
{
    private sealed class FakeProvider(int order, params string[] permissions) : IPermissionProvider
    {
        public int Order => order;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase));
    }

    private static ICurrentUser CreateUser() => AnonymousUser.Instance;

    [Fact]
    public async Task ResolvePermissionsAsync_MergesFromAllProviders()
    {
        var p1 = new FakeProvider(0, "orders.create");
        var p2 = new FakeProvider(100, "orders.approve");
        var composite = new CompositePermissionProvider([p1, p2]);

        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().HaveCount(2);
        result.Should().Contain("orders.create");
        result.Should().Contain("orders.approve");
    }

    [Fact]
    public async Task ResolvePermissionsAsync_DeduplicatesPermissions()
    {
        var p1 = new FakeProvider(0, "orders.create");
        var p2 = new FakeProvider(100, "orders.create", "orders.approve");
        var composite = new CompositePermissionProvider([p1, p2]);

        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResolvePermissionsAsync_NoProviders_ReturnsEmpty()
    {
        var composite = new CompositePermissionProvider([]);

        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvePermissionsAsync_RespectsOrder()
    {
        var p1 = new FakeProvider(200, "b");
        var p2 = new FakeProvider(0, "a");
        var composite = new CompositePermissionProvider([p1, p2]);

        var result = await composite.ResolvePermissionsAsync(CreateUser());

        // Both should be present regardless of order
        result.Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public void Order_IsNegativeOne()
    {
        var composite = new CompositePermissionProvider([]);
        composite.Order.Should().Be(-1);
    }
}
