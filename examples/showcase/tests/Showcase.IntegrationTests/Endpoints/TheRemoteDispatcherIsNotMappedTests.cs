using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     The remote dispatcher <c>/_pragmatic/invoke</c> is mapped only on a host that enables it. This host is called remotely by nobody, so the
///     route does not exist: a host step or middleware that judges a request by its route cannot be walked around through it. The control is the
///     Billing host's <c>TheRemoteDispatcherTests</c>, where the same request reaches the dispatcher.
/// </summary>
public class TheRemoteDispatcherIsNotMappedTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OnAHostNobodyCalls_TheDispatcherIsNotThere()
    {
        var response = await Client.PostAsJsonAsync("/_pragmatic/invoke", new { actionType = "Nowhere.NoSuchAction", payload = new { } }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Action not found",
            "the routing table answered, not a dispatcher looking the action up");
    }
}
