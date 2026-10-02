using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Providers;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     Covers the provider-chain composition behavior of
///     <see cref="CompositePermissionProvider"/> — nested-composite filtering and
///     ordered fan-out of a custom <see cref="IPermissionProvider"/> set.
/// </summary>
public class CompositePermissionProviderChainTests
{
    private sealed class FakeProvider(int order, params string[] permissions) : IPermissionProvider
    {
        public int CallCount { get; private set; }
        public int Order => order;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
        {
            CallCount++;
            return ValueTask.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static ICurrentUser CreateUser() => AnonymousUser.Instance;

    [Fact]
    public async Task Constructor_FiltersOutNestedCompositeProviders()
    {
        // A nested composite is excluded so its inner providers are not double-counted
        // when the same custom providers are also passed at the top level.
        var inner = new FakeProvider(0, "inner.permission");
        var nested = new CompositePermissionProvider([inner]);
        var direct = new FakeProvider(10, "direct.permission");

        var composite = new CompositePermissionProvider([nested, direct]);
        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().ContainSingle().Which.Should().Be("direct.permission");
        inner.CallCount.Should().Be(0, "the nested composite must be filtered out");
        direct.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Constructor_OnlyNestedComposite_ResolvesToEmpty()
    {
        var inner = new FakeProvider(0, "inner.permission");
        var nested = new CompositePermissionProvider([inner]);

        var composite = new CompositePermissionProvider([nested]);
        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().BeEmpty();
        inner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolvePermissionsAsync_InvokesEveryCustomProviderOnce()
    {
        var p1 = new FakeProvider(0, "a");
        var p2 = new FakeProvider(50, "b");
        var p3 = new FakeProvider(100, "c");
        var composite = new CompositePermissionProvider([p3, p1, p2]);

        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().BeEquivalentTo(["a", "b", "c"]);
        p1.CallCount.Should().Be(1);
        p2.CallCount.Should().Be(1);
        p3.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolvePermissionsAsync_MergeIsCaseInsensitive()
    {
        var p1 = new FakeProvider(0, "Orders.Create");
        var p2 = new FakeProvider(10, "orders.create");
        var composite = new CompositePermissionProvider([p1, p2]);

        var result = await composite.ResolvePermissionsAsync(CreateUser());

        result.Should().ContainSingle();
    }
}
