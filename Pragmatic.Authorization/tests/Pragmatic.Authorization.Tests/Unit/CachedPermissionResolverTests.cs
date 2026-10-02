using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

public class CachedPermissionResolverTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

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

    private static ICurrentUser CreateUser(Dictionary<string, IReadOnlyList<string>>? claims = null)
    {
        return new TestUser(claims ?? new Dictionary<string, IReadOnlyList<string>>());
    }

    private sealed class TestUser(Dictionary<string, IReadOnlyList<string>> claims) : ICurrentUser
    {
        public string Id => "test";
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => claims;
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public void HasPermission_WithProvider_ReturnsTrue()
    {
        var provider = new FakeProvider(0, "orders.create");
        var resolver = new CachedPermissionResolver([provider], CreateUser());

        resolver.HasPermission("orders.create").Should().BeTrue();
    }

    [Fact]
    public void HasPermission_MissingPermission_ReturnsFalse()
    {
        var provider = new FakeProvider(0, "orders.read");
        var resolver = new CachedPermissionResolver([provider], CreateUser());

        resolver.HasPermission("orders.create").Should().BeFalse();
    }

    [Fact]
    public void HasPermission_CalledTwice_ResolvesOnlyOnce()
    {
        var provider = new FakeProvider(0, "orders.create");
        var resolver = new CachedPermissionResolver([provider], CreateUser());

        resolver.HasPermission("orders.create");
        resolver.HasPermission("orders.create");

        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public void Permissions_MergesFromMultipleProviders()
    {
        var p1 = new FakeProvider(0, "orders.create");
        var p2 = new FakeProvider(100, "orders.approve");
        var resolver = new CachedPermissionResolver([p2, p1], CreateUser()); // out of order

        resolver.HasPermission("orders.create").Should().BeTrue();
        resolver.HasPermission("orders.approve").Should().BeTrue();
    }

    [Fact]
    public void HasAnyPermission_OneMatch_ReturnsTrue()
    {
        var provider = new FakeProvider(0, "orders.create");
        var resolver = new CachedPermissionResolver([provider], CreateUser());

        resolver.HasAnyPermission(["orders.create", "orders.delete"]).Should().BeTrue();
    }

    [Fact]
    public void HasAllPermissions_AllPresent_ReturnsTrue()
    {
        var provider = new FakeProvider(0, "orders.create", "orders.approve");
        var resolver = new CachedPermissionResolver([provider], CreateUser());

        resolver.HasAllPermissions(["orders.create", "orders.approve"]).Should().BeTrue();
    }

    [Fact]
    public void HasAllPermissions_OneMissing_ReturnsFalse()
    {
        var provider = new FakeProvider(0, "orders.create");
        var resolver = new CachedPermissionResolver([provider], CreateUser());

        resolver.HasAllPermissions(["orders.create", "orders.approve"]).Should().BeFalse();
    }

    [Fact]
    public void Roles_ReadsFromClaims()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["role"] = ["admin", "manager"]
        };
        var resolver = new CachedPermissionResolver([], CreateUser(claims));

        resolver.Roles.Should().Contain("admin").And.Contain("manager");
    }

    [Fact]
    public void IsInRole_MatchingRole_ReturnsTrue()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["role"] = ["admin"]
        };
        var resolver = new CachedPermissionResolver([], CreateUser(claims));

        resolver.IsInRole("admin").Should().BeTrue();
    }

    [Fact]
    public void Groups_ReadsFromClaims()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["group"] = ["engineering"]
        };
        var resolver = new CachedPermissionResolver([], CreateUser(claims));

        resolver.Groups.Should().ContainSingle("engineering");
    }
}
