using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Policy;
using Pragmatic.Authorization.Serialization;
using Xunit;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     <c>ResourcePolicy.RequiresMfa()</c> — the point that reads
///     <c>IAuthenticationContext.IsMfaAuthenticated</c>.
/// </summary>
/// <remarks>
///     The flag is computed from the claims; without a decision point that consults it, MFA could be
///     observed and never required.
/// </remarks>
public class MfaPolicyTests
{
    [Fact]
    public void Evaluate_WithMfaCompleted_Grants()
        => ResourcePolicy.RequiresMfa()
            .Evaluate(PolicyTestHelpers.CreateUser(mfaAuthenticated: true))
            .Should().BeTrue();

    [Fact]
    public void Evaluate_WithoutMfa_Denies()
        => ResourcePolicy.RequiresMfa()
            .Evaluate(PolicyTestHelpers.CreateUser(mfaAuthenticated: false))
            .Should().BeFalse();

    /// <summary>Authenticated is not the same claim as MFA-authenticated, and must not stand in for it.</summary>
    [Fact]
    public void Evaluate_ForAnAuthenticatedUserWithoutMfa_Denies()
        => ResourcePolicy.RequiresMfa()
            .Evaluate(PolicyTestHelpers.CreateUser(isAuthenticated: true))
            .Should().BeFalse();

    /// <summary>The reason it is a policy: it composes instead of standing alone.</summary>
    [Fact]
    public void Evaluate_ComposedWithAPermission_RequiresBoth()
    {
        var policy = ResourcePolicy.RequiresMfa() & ResourcePolicy.RequirePermission("orders.delete");
        var mfaOnly = PolicyTestHelpers.CreateUser(mfaAuthenticated: true);
        var both = PolicyTestHelpers.CreateUser(
            authorization: new PolicyTestHelpers.FakeUserAuthorization(permissions: ["orders.delete"]),
            mfaAuthenticated: true);

        policy.Evaluate(mfaOnly).Should().BeFalse();
        policy.Evaluate(both).Should().BeTrue();
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var expression = PolicySerializer.Serialize(ResourcePolicy.RequiresMfa());

        expression.Type.Should().Be(PolicyExpressionType.Mfa);
        PolicySerializer.Deserialize(expression)
            .Evaluate(PolicyTestHelpers.CreateUser(mfaAuthenticated: true)).Should().BeTrue();
    }
}
