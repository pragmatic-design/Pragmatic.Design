using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Policy;
using Pragmatic.Authorization.Serialization;
using Pragmatic.Identity;
using static Pragmatic.Authorization.Tests.Unit.PolicyTestHelpers;

namespace Pragmatic.Authorization.Tests.Unit;

public class PolicySerializerTests
{
    // =========================================================================
    // Round-trip: simple policies
    // =========================================================================

    [Fact]
    public void RoundTrip_Allow()
    {
        var policy = ResourcePolicy.Allow;
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Allow);
        restored.Evaluate(CreateUser()).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Deny()
    {
        var policy = ResourcePolicy.Deny;
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Deny);
        restored.Evaluate(CreateUser()).Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_Authenticated()
    {
        var expr = PolicySerializer.Serialize(ResourcePolicy.IsAuthenticated());
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Authenticated);
        restored.Evaluate(CreateUser(isAuthenticated: true)).Should().BeTrue();
        restored.Evaluate(CreateUser(isAuthenticated: false)).Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_Permission()
    {
        var policy = ResourcePolicy.RequirePermission("orders.read");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Permission);
        expr.Value.Should().Be("orders.read");

        var auth = new FakeUserAuthorization(permissions: ["orders.read"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_AnyPermission()
    {
        var policy = ResourcePolicy.RequireAnyPermission("a", "b");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.AnyPermission);
        expr.Values.Should().BeEquivalentTo(["a", "b"]);

        var auth = new FakeUserAuthorization(permissions: ["b"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_AllPermissions()
    {
        var policy = ResourcePolicy.RequireAllPermissions("a", "b");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.AllPermissions);

        var auth = new FakeUserAuthorization(permissions: ["a", "b"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Role()
    {
        var policy = ResourcePolicy.InRole("admin");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Role);
        expr.Value.Should().Be("admin");

        var auth = new FakeUserAuthorization(roles: ["admin"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Group()
    {
        var policy = ResourcePolicy.InGroup("engineering");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Group);
        expr.Value.Should().Be("engineering");

        var auth = new FakeUserAuthorization(groups: ["engineering"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Claim_TypeOnly()
    {
        var policy = ResourcePolicy.HasClaim("department");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Claim);
        expr.ClaimType.Should().Be("department");
        expr.Value.Should().BeNull();

        var claims = new Dictionary<string, IReadOnlyList<string>> { ["department"] = ["any"] };
        restored.Evaluate(CreateUser(claims: claims)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Claim_TypeAndValue()
    {
        var policy = ResourcePolicy.HasClaim("department", "engineering");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.ClaimType.Should().Be("department");
        expr.Value.Should().Be("engineering");

        var claims = new Dictionary<string, IReadOnlyList<string>> { ["department"] = ["engineering"] };
        restored.Evaluate(CreateUser(claims: claims)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PrincipalKind()
    {
        var policy = ResourcePolicy.HasPrincipalKind(PrincipalKind.Service);
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.PrincipalKind);
        expr.Value.Should().Be("Service");

        restored.Evaluate(CreateUser(kind: PrincipalKind.Service)).Should().BeTrue();
        restored.Evaluate(CreateUser(kind: PrincipalKind.User)).Should().BeFalse();
    }

    // =========================================================================
    // Round-trip: composite policies
    // =========================================================================

    [Fact]
    public void RoundTrip_And()
    {
        var policy = ResourcePolicy.RequirePermission("a") & ResourcePolicy.RequirePermission("b");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.And);
        expr.Children.Should().HaveCount(2);

        var auth = new FakeUserAuthorization(permissions: ["a", "b"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Or()
    {
        var policy = ResourcePolicy.RequirePermission("a") | ResourcePolicy.RequirePermission("b");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Or);

        var auth = new FakeUserAuthorization(permissions: ["b"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_Not()
    {
        var policy = !ResourcePolicy.InRole("banned");
        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        expr.Type.Should().Be(PolicyExpressionType.Not);
        expr.Children.Should().HaveCount(1);

        var auth = new FakeUserAuthorization(roles: ["user"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_NestedComposite()
    {
        // (isAuth AND hasPerm("read")) OR inRole("admin")
        var policy = (ResourcePolicy.IsAuthenticated() & ResourcePolicy.RequirePermission("read"))
                     | ResourcePolicy.InRole("admin");

        var expr = PolicySerializer.Serialize(policy);
        var restored = PolicySerializer.Deserialize(expr);

        // Admin without "read" permission should pass via OR
        var auth = new FakeUserAuthorization(roles: ["admin"]);
        restored.Evaluate(CreateUser(authorization: auth)).Should().BeTrue();
    }

    // =========================================================================
    // Custom throws
    // =========================================================================

    [Fact]
    public void Serialize_CustomPolicy_ThrowsNotSupported()
    {
        var policy = ResourcePolicy.Custom(_ => true);

        var act = () => PolicySerializer.Serialize(policy);
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*not serializable*");
    }

    // =========================================================================
    // Deserialize malformed
    // =========================================================================

    [Fact]
    public void Deserialize_PermissionMissingValue_Throws()
    {
        var expr = new PolicyExpression { Type = PolicyExpressionType.Permission };

        var act = () => PolicySerializer.Deserialize(expr);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deserialize_NotMissingChildren_Throws()
    {
        var expr = new PolicyExpression { Type = PolicyExpressionType.Not };

        var act = () => PolicySerializer.Deserialize(expr);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deserialize_AndWithOneChild_Throws()
    {
        var expr = new PolicyExpression
        {
            Type = PolicyExpressionType.And,
            Children = [new PolicyExpression { Type = PolicyExpressionType.Allow }]
        };

        var act = () => PolicySerializer.Deserialize(expr);
        act.Should().Throw<ArgumentException>();
    }
}
