using System.Net;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     The distributed topology, started.
/// </summary>
/// <remarks>
///     <para>
///         Three stories closed with "not verified: nothing starts this host". This is that signal.
///         Starting it exercises the two things the generator-level suites can only assert on text:
///         that the container <b>resolves</b>, and that the routes it publishes are the ones it serves.
///     </para>
///     <para>
///         ⚠️ Container validation is not asserted by a line of its own, and could not be: it runs
///         inside <c>builder.Build()</c>, so a registration that cannot be constructed throws before
///         any request. Every case here would fail, and the message would name the service. That is
///         what makes these cases the check.
///     </para>
/// </remarks>
[Collection(DistributedHostCollection.Name)]
public sealed class TheDistributedHostComposesTests(DistributedHostFixture fixture)
{
    /// <summary>
    ///     The host starts, and a route of a module it hosts answers — from the endpoint, not from a
    ///     middleware in front of it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Asserted as <b>success</b> and not as "anything but 404". The first version did the
    ///     latter and passed on a <b>400</b>: this host resolves its tenant from a header
    ///     (<c>UseMultiTenancy(mt => mt.UseHeader())</c>), so a request without one is refused before
    ///     routing ever runs — and a test that accepted it would have proved nothing about the
    ///     endpoint, the invoker behind it, or the database underneath.
    /// </remarks>
    [Fact]
    public async Task ItStartsAndServesAModuleItHosts()
    {
        var response = await Tenant().GetAsync(new Uri("api/properties/search", UriKind.Relative));

        response.IsSuccessStatusCode.Should().BeTrue(
            "Catalog is [Include]d by this host, so its route is mapped, its invoker registered and "
            + "its database reachable — the answer came from the endpoint. Got {0}", response.StatusCode);
    }

    /// <summary>
    ///     End to end: the routes of the module reached over HTTP are served by the host that
    ///     owns it.
    /// </summary>
    /// <remarks>
    ///     Mapped here the route would answer 500, because the invoker the
    ///     generated endpoint injects is deliberately not registered for a remote assembly. The
    ///     generator-level case pins the absence of the <c>MapEndpoint</c> line; this one pins what a
    ///     caller gets.
    ///     <para>
    ///         ⚠️ The control for it is in the other suite and is not duplicated here:
    ///         <c>Showcase.IntegrationTests.RemoteBoundaryTests.RemoteBoundary_BillingEndpoints_AccessibleFromMonolith</c>
    ///         asserts the <b>monolith</b> serves this very route with 200. Without that, "404" would
    ///         also be satisfied by a route that no host anywhere serves.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ItDoesNotServeTheRoutesOfTheModuleItReachesOverHttp()
    {
        var response = await Tenant().GetAsync(new Uri("api/invoices/search", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Billing runs on another host: this one reaches it from code through IBillingActions, and "
            + "publishing a route whose invoker it does not register would answer 500. The same request "
            + "with the same header reaches Catalog's route above, so a 404 here is the routing table "
            + "and not the tenant middleware");
    }

    /// <summary>
    ///     A client carrying what this host asks for before an endpoint ever runs: a tenant and a
    ///     caller with the permission the route requires.
    /// </summary>
    /// <remarks>
    ///     The same headers the monolith suite sends (<c>IntegrationTestBase</c>). Both refusals — 400
    ///     without the tenant, 401 without the identity — happen in front of routing, so without these
    ///     a test asserting "not 404" would be green on a request that never reached the routing table.
    /// </remarks>
    private HttpClient Tenant()
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Distributed host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*");
        return client;
    }
}
