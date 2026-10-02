using System.Net;
using System.Security.Claims;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Pragmatic.Endpoints.AspNetCore.Extensions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The distributed rate limiter must partition anonymous clients by the connection
///     remote IP, never by spoofable forwarding headers.
/// </summary>
public class RateLimitPartitionKeyTests
{
    [Fact]
    public void ResolvePartitionKey_PrefersAuthenticatedIdentity()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice")], authenticationType: "test"));
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        DistributedRateLimiterExtensions.ResolveRateLimitPartitionKey(ctx).Should().Be("alice");
    }

    [Fact]
    public void ResolvePartitionKey_AnonymousUsesRemoteIp()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        DistributedRateLimiterExtensions.ResolveRateLimitPartitionKey(ctx).Should().Be("203.0.113.7");
    }

    [Fact]
    public void ResolvePartitionKey_IgnoresSpoofedForwardingHeaders()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        // A direct client rotating these must NOT obtain a fresh partition.
        ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        ctx.Request.Headers["X-Real-IP"] = "5.6.7.8";

        var key = DistributedRateLimiterExtensions.ResolveRateLimitPartitionKey(ctx);

        key.Should().Be("203.0.113.7");
        key.Should().NotBe("1.2.3.4");
        key.Should().NotBe("5.6.7.8");
    }

    [Fact]
    public void ResolvePartitionKey_NoIdentityNoIp_FallsBackToAnonymous()
    {
        var ctx = new DefaultHttpContext();

        DistributedRateLimiterExtensions.ResolveRateLimitPartitionKey(ctx).Should().Be("anonymous");
    }
}
