using System.Net;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     <c>RequireAuthorizationByDefault</c> is documented as <c>true</c>, and these run the generated
///     root group to see whether that is what a host gets.
/// </summary>
/// <remarks>
///     The first case: a host that never configures the endpoint options has no instance in the
///     container, and the generated root must not read that absence as "off" — if it did, an anonymous
///     write would answer 201. The control case is what keeps the test honest: a root that required
///     authorization unconditionally would pass the first and fail it.
/// </remarks>
public sealed class SecureByDefaultTests
{
    [Fact]
    public async Task PostOrder_WithNoEndpointOptionsRegistered_Returns401ToAnAnonymousCaller()
    {
        await using var factory = new AuthenticatingTestFactory(endpointOptions: null);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/orders?name=Anonymous&amount=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostOrder_WithTheDefaultTurnedOff_Returns201ToAnAnonymousCaller()
    {
        await using var factory = new AuthenticatingTestFactory(
            new PragmaticEndpointsOptions { RequireAuthorizationByDefault = false });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/orders?name=Anonymous&amount=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PostOrder_WithTheDefaultOptionsRegistered_Returns401ToAnAnonymousCaller()
    {
        await using var factory = new AuthenticatingTestFactory(new PragmaticEndpointsOptions());
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/orders?name=Anonymous&amount=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
