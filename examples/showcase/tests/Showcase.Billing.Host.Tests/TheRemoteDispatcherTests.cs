using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Billing.Host.Tests;

/// <summary>
///     The remote dispatcher on the host another host calls: the Billing host enables it (<c>Pragmatic:RemoteBoundaries:InvokeEndpoint:Enabled</c>
///     in its appsettings), because the distributed host invokes Billing's operations over it.
/// </summary>
[Collection(BillingHostCollection.Name)]
public sealed class TheRemoteDispatcherTests(BillingHostFixture fixture)
{
    private static readonly Uri Invoke = new("_pragmatic/invoke", UriKind.Relative);

    /// <summary>The control for the host that does not enable it: here the dispatcher answers, by its own 404 for an action it does not have.</summary>
    [Fact]
    public async Task AnUnknownAction_IsAnsweredByTheDispatcher()
    {
        var response = await fixture.Host.CreateClient().PostAsJsonAsync(Invoke, new { actionType = "Nowhere.NoSuchAction", payload = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Action not found",
            "the dispatcher is mapped and answered, not the routing table");
    }

    /// <summary>A payload that does not deserialize into the action is the caller's error, 400, not a server fault.</summary>
    [Fact]
    public async Task APayloadMissingARequiredMember_IsABadRequest()
    {
        var response = await fixture.Host.CreateClient().PostAsJsonAsync(Invoke,
            new { actionType = "Showcase.Billing.Actions.MarkInvoicePaidAction", payload = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "MarkInvoicePaidAction requires its Id");
    }
}
