using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;
using static Pragmatic.Authorization.Tests.Unit.PolicyTestHelpers;

namespace Pragmatic.Authorization.Tests.Unit;

public class ResourcePolicyTests
{
    // =========================================================================
    // Allow / Deny identity elements
    // =========================================================================

    [Fact]
    public void Allow_AlwaysReturnsTrue()
    {
        var user = CreateUser();
        ResourcePolicy.Allow.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void Deny_AlwaysReturnsFalse()
    {
        var user = CreateUser();
        ResourcePolicy.Deny.Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void Allow_IsSingleton()
    {
        ResourcePolicy.Allow.Should().BeSameAs(ResourcePolicy.Allow);
    }

    [Fact]
    public void Deny_IsSingleton()
    {
        ResourcePolicy.Deny.Should().BeSameAs(ResourcePolicy.Deny);
    }

    // =========================================================================
    // IsAuthenticated
    // =========================================================================

    [Fact]
    public void IsAuthenticated_AuthenticatedUser_ReturnsTrue()
    {
        var user = CreateUser(isAuthenticated: true);
        ResourcePolicy.IsAuthenticated().Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_AnonymousUser_ReturnsFalse()
    {
        var user = CreateUser(isAuthenticated: false);
        ResourcePolicy.IsAuthenticated().Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_IsSingleton()
    {
        ResourcePolicy.IsAuthenticated().Should().BeSameAs(ResourcePolicy.IsAuthenticated());
    }

    // =========================================================================
    // RequirePermission
    // =========================================================================

    [Fact]
    public void RequirePermission_UserHasPermission_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["orders.read"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.RequirePermission("orders.read").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void RequirePermission_UserMissingPermission_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(permissions: ["orders.read"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.RequirePermission("orders.write").Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // RequireAnyPermission
    // =========================================================================

    [Fact]
    public void RequireAnyPermission_HasOne_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["orders.read"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.RequireAnyPermission("orders.read", "orders.write").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void RequireAnyPermission_HasNone_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(permissions: ["orders.delete"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.RequireAnyPermission("orders.read", "orders.write").Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // RequireAllPermissions
    // =========================================================================

    [Fact]
    public void RequireAllPermissions_HasAll_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["orders.read", "orders.write"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.RequireAllPermissions("orders.read", "orders.write").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void RequireAllPermissions_MissingOne_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(permissions: ["orders.read"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.RequireAllPermissions("orders.read", "orders.write").Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // InRole
    // =========================================================================

    [Fact]
    public void InRole_UserInRole_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(roles: ["admin"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.InRole("admin").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void InRole_UserNotInRole_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(roles: ["user"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.InRole("admin").Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // InGroup
    // =========================================================================

    [Fact]
    public void InGroup_UserInGroup_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(groups: ["engineering"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.InGroup("engineering").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void InGroup_UserNotInGroup_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(groups: ["sales"]);
        var user = CreateUser(authorization: auth);
        ResourcePolicy.InGroup("engineering").Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // HasClaim
    // =========================================================================

    [Fact]
    public void HasClaim_ClaimExists_ReturnsTrue()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["department"] = ["engineering"]
        };
        var user = CreateUser(claims: claims);
        ResourcePolicy.HasClaim("department").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void HasClaim_ClaimMissing_ReturnsFalse()
    {
        var user = CreateUser();
        ResourcePolicy.HasClaim("department").Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void HasClaim_WithValue_MatchingValue_ReturnsTrue()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["department"] = ["engineering"]
        };
        var user = CreateUser(claims: claims);
        ResourcePolicy.HasClaim("department", "engineering").Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void HasClaim_WithValue_DifferentValue_ReturnsFalse()
    {
        var claims = new Dictionary<string, IReadOnlyList<string>>
        {
            ["department"] = ["sales"]
        };
        var user = CreateUser(claims: claims);
        ResourcePolicy.HasClaim("department", "engineering").Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // HasPrincipalKind
    // =========================================================================

    [Fact]
    public void HasPrincipalKind_Matches_ReturnsTrue()
    {
        var user = CreateUser(kind: PrincipalKind.Service);
        ResourcePolicy.HasPrincipalKind(PrincipalKind.Service).Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void HasPrincipalKind_Mismatch_ReturnsFalse()
    {
        var user = CreateUser(kind: PrincipalKind.User);
        ResourcePolicy.HasPrincipalKind(PrincipalKind.Service).Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // Custom
    // =========================================================================

    [Fact]
    public void Custom_PredicateTrue_ReturnsTrue()
    {
        var user = CreateUser(kind: PrincipalKind.System);
        ResourcePolicy.Custom(u => u.Kind == PrincipalKind.System).Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void Custom_PredicateFalse_ReturnsFalse()
    {
        var user = CreateUser(kind: PrincipalKind.User);
        ResourcePolicy.Custom(u => u.Kind == PrincipalKind.System).Evaluate(user).Should().BeFalse();
    }
}
