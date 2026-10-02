using System.Security.Claims;
using Pragmatic.Testing.Assertions;
using Pragmatic.Gateway;
using Xunit;

namespace Pragmatic.Gateway.Tests;

/// <summary>
///     <c>JwtOptions.ForwardClaims</c> takes effect: the gateway forwards validated JWT claims
///     to the backend as <c>X-Claim-*</c> headers, stripping any client-supplied copy first (the gateway is
///     the trust boundary).
/// </summary>
public sealed class ClaimForwardingTests
{
    private static HttpRequestMessage NewRequest() => new(HttpMethod.Get, "http://backend/");

    [Fact]
    public void Apply_ForwardsPresentClaims_AsXClaimHeaders()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-1"), new Claim("role", "admin")], "test"));
        using var request = NewRequest();

        ClaimForwarding.Apply(["sub", "role"], user, request.Headers);

        request.Headers.GetValues("X-Claim-sub").Should().ContainSingle().Which.Should().Be("user-1");
        request.Headers.GetValues("X-Claim-role").Should().ContainSingle().Which.Should().Be("admin");
    }

    [Fact]
    public void Apply_StripsClientSuppliedHeader_WhenClaimAbsent()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // no claims
        using var request = NewRequest();
        request.Headers.TryAddWithoutValidation("X-Claim-sub", "forged-by-client");

        ClaimForwarding.Apply(["sub"], user, request.Headers);

        // No authoritative claim → the forged inbound value must not survive.
        request.Headers.Contains("X-Claim-sub").Should().BeFalse();
    }

    [Fact]
    public void Apply_OverwritesClientSuppliedHeader_WithAuthoritativeClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "real-user")], "test"));
        using var request = NewRequest();
        request.Headers.TryAddWithoutValidation("X-Claim-sub", "forged");

        ClaimForwarding.Apply(["sub"], user, request.Headers);

        request.Headers.GetValues("X-Claim-sub").Should().ContainSingle().Which.Should().Be("real-user");
    }
}
