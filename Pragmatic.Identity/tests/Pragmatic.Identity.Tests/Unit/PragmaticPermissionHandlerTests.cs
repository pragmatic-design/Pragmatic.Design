using System.Security.Claims;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Authorization;
using Pragmatic.Identity.Authorization;
using Pragmatic.Endpoints.Authorization;

namespace Pragmatic.Identity.Tests.Unit;

/// <summary>
///     Tests for <see cref="PragmaticPermissionHandler" />.
///     Verifies delegation to IPermissionChecker for All/Any permission modes.
/// </summary>
public class PragmaticPermissionHandlerTests
{
    private sealed class FakePermissionChecker(HashSet<string> grantedPermissions) : IPermissionChecker
    {
        public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken ct = default)
            => new(grantedPermissions.Contains(permission));

        public ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken ct = default)
            => new(permissions.Any(grantedPermissions.Contains));

        public ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken ct = default)
            => new(permissions.All(grantedPermissions.Contains));
    }

    private static PragmaticPermissionHandler CreateHandler(HashSet<string> permissions)
        => new(new FakePermissionChecker(permissions), NullLogger<PragmaticPermissionHandler>.Instance);

    private static AuthorizationHandlerContext CreateContext(bool authenticated, PragmaticPermissionRequirement requirement)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "testuser")], "test")
            : new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        return new AuthorizationHandlerContext([requirement], principal, null);
    }

    // ── All mode ──

    [Fact]
    public async Task HandleAsync_AllMode_UserHasAll_Succeeds()
    {
        var handler = CreateHandler(["orders:read", "orders:list"]);
        var requirement = new PragmaticPermissionRequirement(["orders:read", "orders:list"], PermissionMode.All);
        var context = CreateContext(true, requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_AllMode_UserMissingOne_DoesNotSucceed()
    {
        var handler = CreateHandler(["orders:read"]);
        var requirement = new PragmaticPermissionRequirement(["orders:read", "orders:list"], PermissionMode.All);
        var context = CreateContext(true, requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    // ── Any mode ──

    [Fact]
    public async Task HandleAsync_AnyMode_UserHasOne_Succeeds()
    {
        var handler = CreateHandler(["admin:full"]);
        var requirement = new PragmaticPermissionRequirement(["admin:full", "orders:manage"], PermissionMode.Any);
        var context = CreateContext(true, requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_AnyMode_UserHasNone_DoesNotSucceed()
    {
        var handler = CreateHandler(["users:read"]);
        var requirement = new PragmaticPermissionRequirement(["admin:full", "orders:manage"], PermissionMode.Any);
        var context = CreateContext(true, requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    // ── Unauthenticated ──

    [Fact]
    public async Task HandleAsync_Unauthenticated_DoesNotSucceed()
    {
        var handler = CreateHandler(["orders:read"]);
        var requirement = new PragmaticPermissionRequirement(["orders:read"], PermissionMode.All);
        var context = CreateContext(false, requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
