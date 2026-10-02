using System.Net;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing;
using Xunit;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     Proves the generated authorization contract does what it claims: it passes against an endpoint that
///     enforces its permission and <b>fails</b> against one that does not.
///     <para>
///         Without this, the suite could only show that the contracts are green — which is exactly the state
///         they were in while the unprivileged caller was silently travelling with a wildcard grant and no
///         endpoint was actually being checked. The two fakes below stand in for the application: one honours
///         <c>X-User-Permissions</c>, the other ignores it (fail-open), and the assertion is that the contract
///         tells them apart.
///     </para>
/// </summary>
public class AuthContractDetectionTests
{
    private const string RequiredPermission = "billing.invoice.read";

    /// <summary>Replays what a generated <c>_WithoutRequiredPermission_IsRejected</c> test does, verbatim.</summary>
    private static async Task RunRejectionContractAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices/" + Guid.NewGuid());
        PragmaticTestIdentity.AsUser(request, "contract-noperm");
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        response.ShouldBeRejected();
    }

    /// <summary>Replays what a generated <c>_WithRequiredPermission_IsReachable</c> test does, verbatim.</summary>
    private static async Task RunReachableContractAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices/" + Guid.NewGuid());
        PragmaticTestIdentity.AsUser(request, "contract-" + RequiredPermission, permissions: [RequiredPermission]);
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        response.ShouldNotBeForbidden();
    }

    /// <summary>A client wired like a real contract fixture: shared, and carrying a wildcard default grant.</summary>
    private static HttpClient ClientFor(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        client.DefaultRequestHeaders.Add(PragmaticTestIdentity.TenantIdHeader, "contract-tenant");
        client.DefaultRequestHeaders.Add(PragmaticTestIdentity.PermissionsHeader, "*");
        return client;
    }

    [Fact]
    public async Task RejectionContract_PassesAgainstAnEndpointThatEnforcesItsPermission()
    {
        using var client = ClientFor(new PermissionEnforcingServer(RequiredPermission));

        var act = () => RunRejectionContractAsync(client);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RejectionContract_FailsAgainstAFailOpenEndpoint()
    {
        using var client = ClientFor(new FailOpenServer());

        var act = () => RunRejectionContractAsync(client);

        // This is the whole point of the contract: an endpoint that lets an unprivileged caller through
        // must break the build, not sail past on a 4xx that happened for some other reason.
        (await act.Should().ThrowAsync<PragmaticTestAssertionException>())
            .WithMessage("*rejected*");
    }

    [Fact]
    public async Task ReachableContract_PassesAgainstAnEndpointThatEnforcesItsPermission()
    {
        using var client = ClientFor(new PermissionEnforcingServer(RequiredPermission));

        var act = () => RunReachableContractAsync(client);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReachableContract_FailsWhenTheRequiredPermissionIsNotHonoured()
    {
        // A server demanding a permission the endpoint never declares: the privileged caller is turned away.
        using var client = ClientFor(new PermissionEnforcingServer("some.other.permission"));

        var act = () => RunReachableContractAsync(client);

        await act.Should().ThrowAsync<PragmaticTestAssertionException>();
    }

    /// <summary>Grants access only to a caller whose permission header carries <paramref name="required"/>.</summary>
    private sealed class PermissionEnforcingServer(string required) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var granted = request.Headers.TryGetValues(PragmaticTestIdentity.PermissionsHeader, out var values)
                ? string.Join(",", values).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];

            var status = granted.Contains(required) || granted.Contains("*")
                ? HttpStatusCode.OK
                : HttpStatusCode.Forbidden;

            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
        }
    }

    /// <summary>Answers 200 to anyone — an endpoint whose permission is declared but never enforced.</summary>
    private sealed class FailOpenServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
    }
}
