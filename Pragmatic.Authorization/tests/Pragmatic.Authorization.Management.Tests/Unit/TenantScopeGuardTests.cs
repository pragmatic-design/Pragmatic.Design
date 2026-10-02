using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Authorization.Management;
using Pragmatic.Identity;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Tests.Unit;

/// <summary>
///     Covers the tenant-isolation guard: a tenant-scoped caller must never be able to reach
///     another tenant's data through the flat management permissions, while a global (null-tenant)
///     admin stays unrestricted.
/// </summary>
public sealed class TenantScopeGuardTests
{
    private const string Action = "authorization.roles.manage";

    [Fact]
    public void CheckTenantBinding_TenantScopedCaller_DifferentTargetTenant_ReturnsForbidden()
    {
        var caller = CallerWithTenant("tenant-A");

        var error = TenantScopeGuard.CheckTenantBinding(caller, "tenant-B", Action);

        var forbidden = error.Should().BeOfType<ForbiddenError>().Subject;
        forbidden.Action.Should().Be(Action);
        forbidden.Resource.Should().Be("tenant:tenant-B");
    }

    [Fact]
    public void CheckTenantBinding_TenantScopedCaller_SameTargetTenant_ReturnsNull()
    {
        var caller = CallerWithTenant("tenant-A");

        var error = TenantScopeGuard.CheckTenantBinding(caller, "tenant-A", Action);

        error.Should().BeNull();
    }

    [Fact]
    public void CheckTenantBinding_GlobalCaller_AnyTargetTenant_ReturnsNull()
    {
        // Global admin = null TenantId (AnonymousUser also models a null-tenant principal).
        var caller = CallerWithTenant(null);

        var error = TenantScopeGuard.CheckTenantBinding(caller, "tenant-B", Action);

        error.Should().BeNull();
    }

    [Fact]
    public void CheckTenantBinding_GlobalCaller_ViaAnonymousUser_ReturnsNull()
    {
        var error = TenantScopeGuard.CheckTenantBinding(AnonymousUser.Instance, "tenant-B", Action);

        error.Should().BeNull();
    }

    [Fact]
    public void CheckTenantBinding_NullTargetTenant_TenantScopedCaller_ReturnsForbidden()
    {
        // A null tenant is not "no tenant to check": the stores read a null-tenant role, permission or
        // assignment in EVERY tenant (`TenantId == null || TenantId == tenant`). Letting a tenant-A
        // administrator write one would hand it authority in tenant B.
        var caller = CallerWithTenant("tenant-A");

        var error = TenantScopeGuard.CheckTenantBinding(caller, targetTenantId: null, Action);

        var forbidden = error.Should().BeOfType<ForbiddenError>().Which;
        forbidden.Resource.Should().Be("tenant:global");
    }

    [Fact]
    public void CheckTenantBinding_NullTargetTenant_GlobalCaller_ReturnsNull()
    {
        var caller = CallerWithTenant(null);

        var error = TenantScopeGuard.CheckTenantBinding(caller, targetTenantId: null, Action);

        error.Should().BeNull();
    }

    [Fact]
    public void CheckTenantBinding_MatchIsOrdinalCaseSensitive_DifferentCasing_ReturnsForbidden()
    {
        // Tenant ids are opaque identifiers; a case-fold must NOT silently cross the boundary.
        var caller = CallerWithTenant("tenant-A");

        var error = TenantScopeGuard.CheckTenantBinding(caller, "TENANT-A", Action);

        error.Should().BeOfType<ForbiddenError>();
    }

    private static ICurrentUser CallerWithTenant(string? tenantId)
    {
        var user = new CurrentUserMock();
        user.TenantId.Returns(tenantId);
        return user;
    }
}
