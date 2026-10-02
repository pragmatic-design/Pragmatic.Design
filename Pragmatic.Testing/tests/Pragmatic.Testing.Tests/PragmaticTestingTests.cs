using System.Linq;
using System.Net;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing;
using Xunit;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     Verifies the Pragmatic.Testing Phase-0 harness (#7): typed HTTP assertions and dev-identity headers.
/// </summary>
public class PragmaticTestingTests
{
    private static HttpResponseMessage Response(HttpStatusCode status) => new(status);

    [Fact]
    public void ShouldBeCreated_OnCreated_DoesNotThrow_AndChains()
    {
        var response = Response(HttpStatusCode.Created);

        var returned = response.ShouldBeCreated();

        returned.Should().BeSameAs(response);
    }

    [Fact]
    public void ShouldBeForbidden_OnOk_Throws_WithExpectedAndActual()
    {
        var response = Response(HttpStatusCode.OK);

        var act = () => response.ShouldBeForbidden();

        act.Should().Throw<PragmaticTestAssertionException>()
            .WithMessage("*403*Forbidden*200*OK*");
    }

    [Fact]
    public void ShouldBeSuccess_OnAny2xx_DoesNotThrow()
    {
        Response(HttpStatusCode.Accepted).ShouldBeSuccess().Should().NotBeNull();
    }

    [Fact]
    public void ShouldBeSuccess_OnError_Throws()
    {
        var act = () => Response(HttpStatusCode.BadRequest).ShouldBeSuccess();

        act.Should().Throw<PragmaticTestAssertionException>();
    }

    [Fact]
    public void AsUser_OnRequest_SetsIdentityHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/invoices");

        request.AsUser("user-1", tenantId: "tenant-a", userName: "Tester",
            permissions: ["billing.invoice.read", "billing.invoice.create"]);

        request.Headers.GetValues(PragmaticTestIdentity.UserIdHeader).Single().Should().Be("user-1");
        request.Headers.GetValues(PragmaticTestIdentity.TenantIdHeader).Single().Should().Be("tenant-a");
        request.Headers.GetValues(PragmaticTestIdentity.UserNameHeader).Single().Should().Be("Tester");
        request.Headers.GetValues(PragmaticTestIdentity.PermissionsHeader).Single()
            .Should().Be("billing.invoice.read,billing.invoice.create");
    }

    [Fact]
    public void AsUser_WithoutOptionalParts_OmitsThemButStillWritesAnEmptyPermissionHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");

        request.AsUser("user-2");

        request.Headers.GetValues(PragmaticTestIdentity.UserIdHeader).Single().Should().Be("user-2");
        request.Headers.Contains(PragmaticTestIdentity.UserNameHeader).Should().BeFalse();
        // Tenant falls back to the client default on purpose (a contract test needs the fixture's tenant).
        request.Headers.Contains(PragmaticTestIdentity.TenantIdHeader).Should().BeFalse();
        // Permissions must NOT fall back — see AsUser_WithNoPermissions_SuppressesClientDefaultGrant.
        request.Headers.GetValues(PragmaticTestIdentity.PermissionsHeader).Single().Should().BeEmpty();
    }

    /// <summary>
    ///     Regression for the defect that made every generated "_WithoutRequiredPermission_IsRejected" contract
    ///     test vacuous: the permission header was only written when at least one permission was supplied, so the
    ///     shared fixture's <c>DefaultRequestHeaders</c> wildcard grant reached the server and the supposedly
    ///     unprivileged caller was in fact fully authorized. Asserted end-to-end through a real
    ///     <see cref="HttpClient"/>, because the bug lives in the interaction with its default headers — inspecting
    ///     the request before sending it cannot see it.
    /// </summary>
    [Fact]
    public async Task AsUser_WithNoPermissions_SuppressesClientDefaultGrant()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        client.DefaultRequestHeaders.Add(PragmaticTestIdentity.PermissionsHeader, "*");
        client.DefaultRequestHeaders.Add(PragmaticTestIdentity.TenantIdHeader, "contract-tenant");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/guests/" + Guid.NewGuid());
        request.AsUser("contract-noperm");
        using var _ = await client.SendAsync(request);

        var sent = handler.SentHeaders!;
        sent.GetValues(PragmaticTestIdentity.PermissionsHeader).Single().Should().BeEmpty(
            "the wildcard default must not reach the server for a caller declared without permissions");
        PermissionClaimsFor(sent).Should().BeEmpty();
        // The tenant default is still inherited — only the permission grant is overridden.
        sent.GetValues(PragmaticTestIdentity.TenantIdHeader).Single().Should().Be("contract-tenant");
    }

    [Fact]
    public async Task AsUser_WithPermissions_OverridesClientDefaultGrant()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        client.DefaultRequestHeaders.Add(PragmaticTestIdentity.PermissionsHeader, "*");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/guests");
        request.AsUser("contract-reader", permissions: ["booking.guest.read"]);
        using var _ = await client.SendAsync(request);

        PermissionClaimsFor(handler.SentHeaders!).Should().Equal("booking.guest.read");
    }

    /// <summary>Mirrors how the host's dev identity middleware turns the header into permission claims.</summary>
    private static string[] PermissionClaimsFor(System.Net.Http.Headers.HttpRequestHeaders headers) =>
        !headers.TryGetValues(PragmaticTestIdentity.PermissionsHeader, out var values)
            ? []
            : string.Join(",", values)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public System.Net.Http.Headers.HttpRequestHeaders? SentHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SentHeaders = request.Headers;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }
}
