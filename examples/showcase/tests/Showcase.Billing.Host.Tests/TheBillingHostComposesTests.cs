using System.Net;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Billing.Host.Tests;

/// <summary>
///     The standalone Billing host, started — the side of the distributed topology that owns the module.
/// </summary>
/// <remarks>
///     ⚠️ Container validation is not a case of its own and could not be: it runs inside
///     <c>builder.Build()</c>, so a registration that cannot be constructed throws before any request
///     and every case here fails with the offending service named. That is what makes these the check.
/// </remarks>
[Collection(BillingHostCollection.Name)]
public sealed class TheBillingHostComposesTests(BillingHostFixture fixture)
{
    [Fact]
    public async Task ItStartsAndServesTheModuleItOwns()
    {
        var response = await Caller().GetAsync(new Uri("api/invoices/search", UriKind.Relative));

        response.IsSuccessStatusCode.Should().BeTrue(
            "this host includes BillingModule with a database of its own, so the route is mapped, its "
            + "invoker registered and the database reachable. Got {0}", response.StatusCode);
    }

    /// <summary>
    ///     The control: a route of a module this host does not have at all.
    /// </summary>
    /// <remarks>
    ///     Without it, "Billing answers" does not tell a host that serves its own module apart from one
    ///     that serves whatever it can find on the compilation — which is exactly the failure mode the
    ///     [Include] filter exists to prevent, and which this example's sibling had.
    /// </remarks>
    [Fact]
    public async Task ItDoesNotServeAModuleItDoesNotHave()
    {
        var response = await Caller().GetAsync(new Uri("api/properties/search", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Catalog is not part of this host: it is served by the one that includes it");
    }

    /// <summary>A caller this host accepts: identity and the permission the route asks for.</summary>
    /// <remarks>
    ///     ⚠️ Both refusals are in front of routing — 401 without an identity — so without these headers
    ///     a "not 404" assertion would be green on a request that never reached the routing table.
    /// </remarks>
    private HttpClient Caller()
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Billing host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "billing.*");
        return client;
    }
}
