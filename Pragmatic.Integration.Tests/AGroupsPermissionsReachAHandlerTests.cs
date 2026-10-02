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
///     A route group's <c>RequiredPermissions</c> reach the authorization pipeline as a
///     <c>PragmaticPermissionRequirement</c> — a requirement something can read, and does.
/// </summary>
/// <remarks>
///     <para>
///         An opaque <c>RequireAssertion</c> comparing the token's <c>permission</c> claims by hand
///         would not do: nothing reading endpoint metadata could say what the group required, the 403
///         would come back without its <c>requiredPermissions</c> — the result handler reads those off
///         the requirement — and the comparison would never ask <c>IPermissionChecker</c>, so roles and
///         wildcards would not expand.
///     </para>
///     <para>
///         ⚠️ No application in this repository configures a group's permissions, so without this test
///         the enforcement is generated code with no caller. That is why this test exists at the HTTP
///         end and not only on the generated text: a shape nobody runs is how an opaque gate survives.
///     </para>
///     <para>
///         The handler here is the test's own, deliberately: what is being proved is that the
///         requirement arrives <b>with its permissions legible</b>, which is the property an opaque
///         assertion does not have. Whether Identity's handler then expands roles through
///         <c>IPermissionChecker</c> is that handler's own contract, proved in its own tests.
///     </para>
/// </remarks>
public sealed class AGroupsPermissionsReachAHandlerTests
{
    private const string Required = "reports.monthly.read";

    [Fact]
    public async Task TheGroupsPermissions_ArriveAsARequirementAHandlerCanRead()
    {
        var seen = new RecordingHandler();
        await using var factory = WithGroupPermissions(seen, granting: true);

        var response = await GetMonthlyReport(factory);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        seen.Requirements.Should().ContainSingle("the group's gate is one requirement, not an assertion")
            .Which.Permissions.Should().Equal([Required],
                "and it names what the group requires, which is what makes it inspectable");
        seen.Requirements[0].Mode.Should().Be(PermissionMode.All);
    }

    /// <summary>
    ///     The control: the same route refused, by the same requirement. A gate that only ever grants is
    ///     not a gate, and the generated code is the same in both runs.
    /// </summary>
    [Fact]
    public async Task TheSameRequirement_RefusesWhenTheHandlerDoesNot()
    {
        var seen = new RecordingHandler();
        await using var factory = WithGroupPermissions(seen, granting: false);

        var response = await GetMonthlyReport(factory);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        seen.Requirements.Should().ContainSingle("the requirement was evaluated, and said no");
    }

    /// <summary>
    ///     The second control: a group whose options carry no permissions reaches no such requirement —
    ///     the route is open to an authenticated caller, as it was before any of this.
    /// </summary>
    [Fact]
    public async Task AGroupThatRequiresNothing_ReachesNoRequirement()
    {
        var seen = new RecordingHandler();
        await using var factory = new AuthenticatingTestFactory(
            new PragmaticEndpointsOptions(), services: s => s.AddSingleton<IAuthorizationHandler>(seen));

        var response = await GetMonthlyReport(factory);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        seen.Requirements.Should().BeEmpty("no permissions were configured, so none are required");
    }

    private static AuthenticatingTestFactory WithGroupPermissions(RecordingHandler handler, bool granting)
    {
        // The key the generated registration looks up: the group's simple name without its "Group"
        // suffix, which is what EndpointRuntimeConfigRenderer emits. Read from the generator rather
        // than guessed — the full type name silently matches nothing, and the test would then pass its
        // controls and prove the opposite of what it says.
        var options = new PragmaticEndpointsOptions()
            .ConfigureGroup("Reports", group => group.RequiredPermissions.Add(Required));

        handler.Grants = granting;

        return new AuthenticatingTestFactory(
            options, services: s => s.AddSingleton<IAuthorizationHandler>(handler));
    }

    private static async Task<HttpResponseMessage> GetMonthlyReport(AuthenticatingTestFactory factory)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reports/monthly");
        request.Headers.Add(NoCredentialsAuthenticationHandler.UserHeader, "someone");

        return await client.SendAsync(request);
    }

    /// <summary>
    ///     Records every <c>PragmaticPermissionRequirement</c> the pipeline hands it, and grants or
    ///     refuses on demand.
    /// </summary>
    private sealed class RecordingHandler : AuthorizationHandler<PragmaticPermissionRequirement>
    {
        public List<PragmaticPermissionRequirement> Requirements { get; } = [];

        public bool Grants { get; set; } = true;

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PragmaticPermissionRequirement requirement)
        {
            Requirements.Add(requirement);
            if (Grants)
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}
