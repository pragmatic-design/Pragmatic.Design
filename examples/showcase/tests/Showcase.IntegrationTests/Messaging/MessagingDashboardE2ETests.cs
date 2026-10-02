using System.Net;
using System.Text;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Showcase.Booking.Events;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Messaging;

/// <summary>
///     Operations dashboard E2E on the REAL Showcase host: a dead letter forced into the store →
///     replay through the API → the event goes through the bus again and the Billing handler creates
///     the invoice. The panel and the API answer under /_messaging (config-driven).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MessagingDashboardE2ETests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string DashboardApiKey = "test-dashboard-key";

    private ShowcaseWebFactory? _factory;
    private HttpClient? _client;

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public Task InitializeAsync()
    {
        // The dashboard's key-less mode is loopback-only and refuses a request whose remote IP cannot be
        // positively confirmed as loopback (TestServer leaves it null). An out-of-process
        // caller is exactly the case that must present a key, so configure one and send it.
        _factory = new ShowcaseWebFactory(postgres, new Dictionary<string, string?>
        {
            ["Pragmatic:Messaging:Dashboard:Enabled"] = "true",
            ["Pragmatic:Messaging:Dashboard:ApiKey"] = DashboardApiKey,
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Messaging-Key", DashboardApiKey);
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        _client.DefaultRequestHeaders.Add("X-User-Name", "Dashboard Test");
        _client.DefaultRequestHeaders.Add("X-User-Permissions", "billing.invoice.read,billing.invoice.view-all");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Panel_And_Status_AnswerUnderMessagingPath()
    {
        var panel = await _client!.GetAsync("/_messaging/panel");
        panel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await panel.Content.ReadAsStringAsync()).Should().Contain("Pragmatic Messaging");

        var status = await _client.GetAsync("/_messaging/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("transport").GetString().Should().Be("Channels");
    }

    [Fact]
    public async Task DeadLetter_Replay_ReprocessesEventAndCreatesInvoice()
    {
        // Force a REAL dead letter into the store: a ReservationConfirmed serialized with the app's
        // serializer (same format as the pipeline).
        var reservationId = Guid.NewGuid();
        var confirmed = new ReservationConfirmed(
            ReservationId: reservationId,
            GuestId: Guid.NewGuid(),
            PropertyId: Guid.NewGuid(),
            CheckIn: DateTimeOffset.UtcNow.AddDays(1),
            CheckOut: DateTimeOffset.UtcNow.AddDays(3),
            TotalAmount: 180m,
            Currency: "EUR",
            OccurredAt: DateTimeOffset.UtcNow);

        var serializer = _factory!.Services.GetRequiredService<IMessageSerializer>();
        var payload = Encoding.UTF8.GetString(serializer.Serialize(confirmed, typeof(ReservationConfirmed)));

        var store = _factory.Services.GetRequiredService<IDeadLetterStore>();
        var deadLetter = new DeadLetterMessage(
            typeof(ReservationConfirmed).FullName!, payload,
            "simulated poison handler", 3, MessageContext.New(), DateTimeOffset.UtcNow);
        await store.StoreAsync(deadLetter);

        // The dead letter is visible through the API.
        var list = JsonDocument.Parse(await _client!.GetStringAsync("/_messaging/dead-letters"));
        list.RootElement.EnumerateArray()
            .Any(e => e.GetProperty("id").GetGuid() == deadLetter.Id)
            .Should().BeTrue();

        // Replay: ripubblica sul bus (Channels) → consumer → Billing handler → fattura.
        var replay = await _client.PostAsync($"/_messaging/dead-letters/{deadLetter.Id}/replay", content: null);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        (await store.GetAsync(deadLetter.Id)).Should().BeNull("the replay removes the dead letter");

        // The reprocessing really happened: the Billing handler created the invoice.
        JsonElement? invoice = null;
        for (var attempt = 0; attempt < 40 && invoice is null; attempt++)
        {
            await Task.Delay(250);
            var search = JsonDocument.Parse(await _client.GetStringAsync(
                $"/api/invoices/search?reservationId={reservationId}"));
            var items = search.RootElement.GetProperty("items");
            if (items.GetArrayLength() > 0)
                invoice = items[0];
        }

        invoice.Should().NotBeNull("the replayed event must go through the bus and create the invoice");
        invoice!.Value.GetProperty("reservationId").GetGuid().Should().Be(reservationId);
    }
}
