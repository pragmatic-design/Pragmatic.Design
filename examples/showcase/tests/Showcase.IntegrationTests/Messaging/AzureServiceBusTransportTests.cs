using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Showcase.Booking.Events;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Messaging;

/// <summary>
///     Boots the REAL Showcase host with the Azure Service Bus transport against the official
///     emulator (Testcontainers) and proves a cross-boundary event travels through the broker:
///     publish <see cref="ReservationConfirmed"/> on the bus → ASB topic <c>booking.events</c> →
///     consumer service → Billing handler → draft invoice on PostgreSQL, verified via HTTP.
///     Entities are pre-provisioned by the fixture (the emulator has no management API) — the
///     exact pass-through mode used with IaC-provisioned namespaces.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AzureServiceBusTransportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly AzureServiceBusEmulatorFixture _emulator = new();
    private ShowcaseWebFactory? _factory;
    private HttpClient? _client;

#pragma warning disable CA2007 // xUnit manages SynchronizationContext
    public async Task InitializeAsync()
    {
        await _emulator.InitializeAsync();
        if (_emulator.ConnectionString is null)
            return; // Docker unavailable — tests skip gracefully

        _factory = new ShowcaseWebFactory(postgres, new Dictionary<string, string?>
        {
            ["Pragmatic:Messaging:Transport"] = "AzureServiceBus",
            ["Pragmatic:Messaging:AzureServiceBus:ConnectionString"] = _emulator.ConnectionString,
            ["Pragmatic:Messaging:AzureServiceBus:AutoCreateEntities"] = "false",
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        _client.DefaultRequestHeaders.Add("X-User-Name", "ASB Test");
        _client.DefaultRequestHeaders.Add("X-User-Permissions", "billing.invoice.read,billing.invoice.view-all");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _emulator.DisposeAsync();
    }

    [Fact]
    public async Task ReservationConfirmed_TravelsThroughTheBroker_AndCreatesInvoice()
    {
        if (_factory is null)
            return; // Docker unavailable

        // Probe: is the WAF override visible in the DI configuration (lazy) at all?
        var cfg = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        cfg["Pragmatic:Messaging:Transport"].Should().Be("AzureServiceBus", "the WAF in-memory override must reach the app configuration");

        // The active transport is really Azure Service Bus.
        _factory.Services.GetRequiredService<IMessageTransport>().Name.Should().Be("AzureServiceBus");

        // Publish the cross-boundary event ON THE BUS: TransportAwareMessageBus → emulator
        // topic booking.events → AzureServiceBusConsumerService → Billing handler.
        var reservationId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new ReservationConfirmed(
                ReservationId: reservationId,
                GuestId: Guid.NewGuid(),
                PropertyId: Guid.NewGuid(),
                CheckIn: DateTimeOffset.UtcNow.AddDays(1),
                CheckOut: DateTimeOffset.UtcNow.AddDays(3),
                TotalAmount: 250m,
                Currency: "EUR",
                OccurredAt: DateTimeOffset.UtcNow));
        }

        // The broker roundtrip is asynchronous: poll for the invoice the handler creates.
        var found = false;
        for (var i = 0; i < 60 && !found; i++)
        {
            await Task.Delay(500);
            var response = await _client!.GetAsync($"/api/invoices/search?reservationId={reservationId}");
            if (!response.IsSuccessStatusCode)
                continue;

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            found = payload.TryGetProperty("items", out var items) && items.GetArrayLength() >= 1;
        }

        found.Should().BeTrue(
            "ReservationConfirmed published on the bus must reach the Billing handler through the real ASB emulator");
    }
}
