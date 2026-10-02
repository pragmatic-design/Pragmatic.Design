using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     What <c>[ResponseCache]</c> in its default, shared form keeps — measured through the generated
///     endpoint, the way an application would turn the output cache on.
/// </summary>
/// <remarks>
///     The shared form generates an ASP.NET <c>CacheOutput</c> policy. Whether that policy keeps the
///     answers of authenticated callers decides everything a reader should be told about it: if it does,
///     one user's response is served to another; if it does not, the attribute is a no-op on every
///     authenticated route. Both were written down in this repository as fact; this is the measurement.
/// </remarks>
public sealed class WhatASharedResponseCacheKeepsTests
{
    private const string Route = "/api/rates/shared";

    /// <summary>The control: with the output cache on, an anonymous answer is kept.</summary>
    /// <remarks>Without it, every other case here could be "nothing is cached" for a reason of its own.</remarks>
    [Fact]
    public async Task AnAnonymousAnswer_IsKept()
    {
        await using var host = new AuthenticatingTestFactory(endpointOptions: null, outputCache: true);
        var client = host.CreateClient();

        var first = await client.GetStringAsync(Route);
        var second = await client.GetStringAsync(Route);

        ReadsOf(host).Should().Be(1, "the second anonymous request is the cached answer");
        second.Should().Be(first);
    }

    [Fact]
    public async Task AnAuthenticatedAnswer_IsNotKept()
    {
        await using var host = new AuthenticatingTestFactory(endpointOptions: null, outputCache: true);

        await GetAsAsync(host, "alice");
        await GetAsAsync(host, "alice");

        ReadsOf(host).Should().Be(2,
            "ASP.NET's default output-cache policy does not cache a request whose user is authenticated");
    }

    [Fact]
    public async Task OneUsersAnswer_IsNotServedToAnother()
    {
        await using var host = new AuthenticatingTestFactory(endpointOptions: null, outputCache: true);

        await GetAsAsync(host, "alice");
        var bob = await GetAsAsync(host, "bob");

        bob.Should().StartWith("\"bob|", "bob's answer is computed for bob");
    }

    /// <summary>
    ///     Varying by the header that names the caller does not make an authenticated answer cacheable:
    ///     the policy that refuses it is still in front.
    /// </summary>
    [Fact]
    public async Task VaryingByTheCaller_DoesNotMakeAnAuthenticatedAnswerKept()
    {
        await using var host = new AuthenticatingTestFactory(endpointOptions: null, outputCache: true);
        var anonymous = host.CreateClient();

        await anonymous.GetStringAsync(PerCallerRoute);
        await anonymous.GetStringAsync(PerCallerRoute);
        ReadsOf(host).Should().Be(1, "the control: this route's policy does cache");

        await GetAsAsync(host, "alice", PerCallerRoute);
        await GetAsAsync(host, "alice", PerCallerRoute);

        ReadsOf(host).Should().Be(3, "each of alice's requests ran the body");
    }

    /// <summary>Without <c>UseOutputCache()</c> the declaration keeps nothing, for anybody.</summary>
    [Fact]
    public async Task WithoutTheOutputCacheMiddleware_NothingIsKept()
    {
        await using var host = new AuthenticatingTestFactory(endpointOptions: null);
        var client = host.CreateClient();

        await client.GetStringAsync(Route);
        await client.GetStringAsync(Route);

        ReadsOf(host).Should().Be(2);
    }

    private const string PerCallerRoute = "/api/rates/per-caller";

    private static async Task<string> GetAsAsync(AuthenticatingTestFactory host, string user, string route = Route)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(NoCredentialsAuthenticationHandler.UserHeader, user);
        return await client.GetStringAsync(route);
    }

    private static int ReadsOf(AuthenticatingTestFactory host)
        => host.Services.GetRequiredService<IRateSource>().Reads;
}
