using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Policy;
using static Pragmatic.Authorization.Tests.Unit.PolicyTestHelpers;

namespace Pragmatic.Authorization.Tests.Unit;

public class ResourcePolicyCompositionTests
{
    // =========================================================================
    // AND
    // =========================================================================

    [Fact]
    public void And_BothTrue_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["a", "b"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a").And(ResourcePolicy.RequirePermission("b"));
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void And_LeftFalse_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(permissions: ["b"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a").And(ResourcePolicy.RequirePermission("b"));
        policy.Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void And_RightFalse_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization(permissions: ["a"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a").And(ResourcePolicy.RequirePermission("b"));
        policy.Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void AndOperator_Works()
    {
        var auth = new FakeUserAuthorization(permissions: ["a", "b"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a") & ResourcePolicy.RequirePermission("b");
        policy.Evaluate(user).Should().BeTrue();
    }

    // =========================================================================
    // OR
    // =========================================================================

    [Fact]
    public void Or_BothTrue_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["a", "b"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a").Or(ResourcePolicy.RequirePermission("b"));
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void Or_OnlyLeftTrue_ReturnsTrue()
    {
        var auth = new FakeUserAuthorization(permissions: ["a"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a").Or(ResourcePolicy.RequirePermission("b"));
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void Or_BothFalse_ReturnsFalse()
    {
        var auth = new FakeUserAuthorization();
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a").Or(ResourcePolicy.RequirePermission("b"));
        policy.Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void OrOperator_Works()
    {
        var auth = new FakeUserAuthorization(permissions: ["b"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.RequirePermission("a") | ResourcePolicy.RequirePermission("b");
        policy.Evaluate(user).Should().BeTrue();
    }

    // =========================================================================
    // NOT
    // =========================================================================

    [Fact]
    public void Not_InvertsTrue()
    {
        var user = CreateUser(isAuthenticated: true);
        var policy = ResourcePolicy.IsAuthenticated().Not();
        policy.Evaluate(user).Should().BeFalse();
    }

    [Fact]
    public void Not_InvertsFalse()
    {
        var user = CreateUser(isAuthenticated: false);
        var policy = ResourcePolicy.IsAuthenticated().Not();
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void NotOperator_Works()
    {
        var user = CreateUser(isAuthenticated: false);
        var policy = !ResourcePolicy.IsAuthenticated();
        policy.Evaluate(user).Should().BeTrue();
    }

    // =========================================================================
    // Nested composition
    // =========================================================================

    [Fact]
    public void NestedAndOr_EvaluatesCorrectly()
    {
        // (hasPermA AND isAuthenticated) OR inRole("admin")
        var auth = new FakeUserAuthorization(permissions: ["a"], roles: ["user"]);
        var user = CreateUser(isAuthenticated: true, authorization: auth);

        var policy = (ResourcePolicy.RequirePermission("a") & ResourcePolicy.IsAuthenticated())
                     | ResourcePolicy.InRole("admin");

        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void NestedAndOr_FallsBackToOr()
    {
        // (hasPermA AND hasPermB) OR inRole("admin")
        var auth = new FakeUserAuthorization(permissions: ["a"], roles: ["admin"]);
        var user = CreateUser(authorization: auth);

        var policy = (ResourcePolicy.RequirePermission("a") & ResourcePolicy.RequirePermission("b"))
                     | ResourcePolicy.InRole("admin");

        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void DoubleNot_ReturnsOriginal()
    {
        var user = CreateUser(isAuthenticated: true);
        var policy = !!ResourcePolicy.IsAuthenticated();
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void ComplexComposition_NotAndOr()
    {
        // NOT(inRole("banned")) AND (hasPerm("read") OR hasPerm("admin"))
        var auth = new FakeUserAuthorization(permissions: ["read"], roles: ["user"]);
        var user = CreateUser(authorization: auth);

        var policy = !ResourcePolicy.InRole("banned")
                     & (ResourcePolicy.RequirePermission("read") | ResourcePolicy.RequirePermission("admin"));

        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void ComplexComposition_BannedUser_Denied()
    {
        // NOT(inRole("banned")) AND hasPerm("read")
        var auth = new FakeUserAuthorization(permissions: ["read"], roles: ["banned"]);
        var user = CreateUser(authorization: auth);

        var policy = !ResourcePolicy.InRole("banned") & ResourcePolicy.RequirePermission("read");
        policy.Evaluate(user).Should().BeFalse();
    }

    // =========================================================================
    // Identity element behavior
    // =========================================================================

    [Fact]
    public void Allow_And_Policy_ReturnsPolicy()
    {
        var auth = new FakeUserAuthorization(permissions: ["read"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.Allow & ResourcePolicy.RequirePermission("read");
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void Deny_Or_Policy_ReturnsPolicy()
    {
        var auth = new FakeUserAuthorization(permissions: ["read"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.Deny | ResourcePolicy.RequirePermission("read");
        policy.Evaluate(user).Should().BeTrue();
    }

    [Fact]
    public void Deny_And_Policy_AlwaysFalse()
    {
        var auth = new FakeUserAuthorization(permissions: ["read"]);
        var user = CreateUser(authorization: auth);

        var policy = ResourcePolicy.Deny & ResourcePolicy.RequirePermission("read");
        policy.Evaluate(user).Should().BeFalse();
    }
}
