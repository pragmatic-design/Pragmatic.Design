using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization;

namespace Pragmatic.Identity.Tests.Unit;

/// <summary>
///     Tests for <see cref="ClaimsPermissionChecker" />.
///     Verifies delegation to ICurrentUser.Authorization permission methods.
/// </summary>
public class ClaimsPermissionCheckerTests
{
    private sealed class FakeAuthorization(HashSet<string> permissions) : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => permissions;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> perms) => perms.Any(permissions.Contains);
        public bool HasAllPermissions(IEnumerable<string> perms) => perms.All(permissions.Contains);
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    private sealed class FakeUser(HashSet<string> permissions) : ICurrentUser
    {
        public string Id => "user-1";
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => new FakeAuthorization(permissions);
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    [Fact]
    public async Task HasPermissionAsync_UserHasPermission_ReturnsTrue()
    {
        var checker = new ClaimsPermissionChecker(new FakeUser(["orders.create"]));

        var result = await checker.HasPermissionAsync("orders.create");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_UserLacksPermission_ReturnsFalse()
    {
        var checker = new ClaimsPermissionChecker(new FakeUser(["orders.read"]));

        var result = await checker.HasPermissionAsync("orders.create");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAnyPermissionAsync_UserHasOne_ReturnsTrue()
    {
        var checker = new ClaimsPermissionChecker(new FakeUser(["orders.admin"]));

        var result = await checker.HasAnyPermissionAsync(["orders.create", "orders.admin"]);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAnyPermissionAsync_UserHasNone_ReturnsFalse()
    {
        var checker = new ClaimsPermissionChecker(new FakeUser(["orders.read"]));

        var result = await checker.HasAnyPermissionAsync(["orders.create", "orders.admin"]);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAllPermissionsAsync_UserHasAll_ReturnsTrue()
    {
        var checker = new ClaimsPermissionChecker(new FakeUser(["orders.create", "orders.approve"]));

        var result = await checker.HasAllPermissionsAsync(["orders.create", "orders.approve"]);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAllPermissionsAsync_UserMissingOne_ReturnsFalse()
    {
        var checker = new ClaimsPermissionChecker(new FakeUser(["orders.create"]));

        var result = await checker.HasAllPermissionsAsync(["orders.create", "orders.approve"]);

        result.Should().BeFalse();
    }
}
