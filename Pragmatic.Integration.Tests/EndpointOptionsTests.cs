using System.Net;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     <c>DefaultAuthorizationPolicy</c>, through the generated root group. <c>EnableOpenApi</c> is
///     proved on the document it changes, in <see cref="TheRuntimeDocumentTests" />.
/// </summary>
/// <remarks>
///     Both were declared, documented, and read by nothing: a host that set either got the default
///     behaviour, a green build, and no word.
/// </remarks>
public sealed class EndpointOptionsTests
{
    private const string AdminsPolicy = "Admins";

    private static AuthenticatingTestFactory WithAdminsPolicy(PragmaticEndpointsOptions options)
        => new(options, authorization => authorization.AddPolicy(AdminsPolicy, p => p.RequireRole("admin")));

    private static async Task<HttpResponseMessage> PostOrderAs(AuthenticatingTestFactory factory, string? roles)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders?name=Policy&amount=1");
        request.Headers.Add(NoCredentialsAuthenticationHandler.UserHeader, "someone");
        if (roles is not null)
            request.Headers.Add(NoCredentialsAuthenticationHandler.RolesHeader, roles);

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task DefaultAuthorizationPolicy_RefusesAnAuthenticatedUserWhoFailsIt()
    {
        await using var factory = WithAdminsPolicy(new PragmaticEndpointsOptions { DefaultAuthorizationPolicy = AdminsPolicy });

        var response = await PostOrderAs(factory, roles: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the root applies the named policy, and this caller does not meet it");
    }

    [Fact]
    public async Task DefaultAuthorizationPolicy_LetsThroughAUserWhoMeetsIt()
    {
        await using var factory = WithAdminsPolicy(new PragmaticEndpointsOptions { DefaultAuthorizationPolicy = AdminsPolicy });

        var response = await PostOrderAs(factory, roles: "admin");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The control: unset, the root asks only for an authenticated user.</summary>
    [Fact]
    public async Task WithoutADefaultPolicy_AnAuthenticatedUserWithoutTheRoleGetsThrough()
    {
        await using var factory = WithAdminsPolicy(new PragmaticEndpointsOptions());

        var response = await PostOrderAs(factory, roles: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "with no default policy the policy the host declares is not applied to every endpoint");
    }
}
