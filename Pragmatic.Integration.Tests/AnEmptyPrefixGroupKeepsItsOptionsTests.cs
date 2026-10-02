using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Authorization;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     The options <c>ConfigureGroup</c> sets on a group with an empty prefix reach its
///     endpoints.
/// </summary>
/// <remarks>
///     <para>
///         A group with an empty prefix still needs a <c>MapGroup</c> of its own, because that is what
///         carries the group's runtime options. Mapped onto the root,
///         <see cref="Domain.Actions.SharedGroup" /> would lose them and the route would answer as if
///         none were set.
///     </para>
///     <para>
///         ⚠️ Asserted with a group permission and not with <c>RequireAuthorization</c>: generated
///         endpoints already require an authenticated caller by default, so an anonymous call answers 401
///         whether the group's option arrived or not, and the test would pass for the wrong reason.
///     </para>
/// </remarks>
public sealed class AnEmptyPrefixGroupKeepsItsOptionsTests
{
    private const string Required = "shared.report.read";

    [Fact]
    public async Task TheGroupsPermission_IsRequiredOnItsEndpoint()
    {
        var seen = new RefusingHandler();
        var options = new PragmaticEndpointsOptions()
            .ConfigureGroup("Shared", group => group.RequiredPermissions.Add(Required));
        await using var factory = new AuthenticatingTestFactory(
            options, services: s => s.AddSingleton<IAuthorizationHandler>(seen));

        var response = await GetSharedReport(factory);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the group's RequiredPermissions reached the endpoint, and the handler refused them");
        seen.Requirements.Should().ContainSingle()
            .Which.Permissions.Should().Equal([Required]);
    }

    /// <summary>
    ///     The control: the same endpoint with no options for the group answers — so the 403 above comes
    ///     from the group's option and not from something else on the route.
    /// </summary>
    [Fact]
    public async Task WithoutOptionsForTheGroup_TheEndpointAnswers()
    {
        var seen = new RefusingHandler();
        await using var factory = new AuthenticatingTestFactory(
            new PragmaticEndpointsOptions(), services: s => s.AddSingleton<IAuthorizationHandler>(seen));

        var response = await GetSharedReport(factory);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        seen.Requirements.Should().BeEmpty();
    }

    private static async Task<HttpResponseMessage> GetSharedReport(AuthenticatingTestFactory factory)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shared-report");
        request.Headers.Add(NoCredentialsAuthenticationHandler.UserHeader, "someone");

        return await client.SendAsync(request);
    }

    /// <summary>Records every <c>PragmaticPermissionRequirement</c> and refuses it.</summary>
    private sealed class RefusingHandler : AuthorizationHandler<PragmaticPermissionRequirement>
    {
        public List<PragmaticPermissionRequirement> Requirements { get; } = [];

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PragmaticPermissionRequirement requirement)
        {
            Requirements.Add(requirement);
            return Task.CompletedTask;
        }
    }
}
