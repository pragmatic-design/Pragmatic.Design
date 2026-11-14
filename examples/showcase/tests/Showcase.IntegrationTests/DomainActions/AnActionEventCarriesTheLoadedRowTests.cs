using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Events.Extensions;
using Pragmatic.Testing.Assertions;
using Showcase.Billing.Events;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     The event <c>MarkInvoicePaidAction</c> declares with <c>[Raises&lt;T&gt;]</c> arrives with the members of the invoice it loaded (#737).
/// </summary>
/// <remarks>
///     <para>
///         The action's inputs are the invoice's id and the payer; the event's number and total are only on the loaded row. An action's
///         event was filled from its inputs alone, and those two members were the empty string and zero, reported by <c>PRAG0433</c>.
///     </para>
///     <para>
///         The handler is registered by this test on a host of its own, so what it records is this host's dispatch and nothing else's.
///         The control is the input member, which arrived before too.
///     </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AnActionEventCarriesTheLoadedRowTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly ConcurrentQueue<InvoicePaymentRecorded> _received = new();
    private ShowcaseWebFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public Task InitializeAsync()
    {
        _factory = new ShowcaseWebFactory(
            fixture,
            extraServices: services =>
            {
                services.AddSingleton(_received);
                services.AddDomainEventHandler<Recorder, InvoicePaymentRecorded>();
            });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        _client.DefaultRequestHeaders.Add("X-User-Name", "Action Event Test");
        _client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*,billing.*");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ThePaymentEvent_CarriesTheInvoicesNumberAndTotal()
    {
        var invoice = await AnIssuedInvoiceAsync();

        var paid = await _client.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("id").GetGuid()}/pay", new { }, JsonOptions);
        paid.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var recorded = _received.Should().ContainSingle("the action declares one event and committed once").Which;
        recorded.InvoiceId.Should().Be(invoice.GetProperty("id").GetGuid(), "the key of the loaded row");
        recorded.InvoiceNumber.Should().Be(invoice.GetProperty("invoiceNumber").GetString(), "only the loaded row has the number");
        recorded.TotalAmount.Should().Be(invoice.GetProperty("totalAmount").GetDecimal(), "only the loaded row has the total");
        recorded.InvoiceNumber.Should().StartWith("INV-");
    }

    /// <summary>A confirmed reservation, whose confirmation creates its invoice.</summary>
    private async Task<JsonElement> AnIssuedInvoiceAsync()
    {
        var guest = await PostAsync("/api/guests", new { firstName = "Event", lastName = "Payer", email = $"pay.{Guid.NewGuid():N}@test.com" });
        var property = await PostAsync("/api/properties", new
        {
            code = $"EV-{Guid.NewGuid():N}"[..12], name = $"EvProp-{Guid.NewGuid():N}"[..20], city = "Rome", country = "IT", starRating = 3
        });
        var propertyId = property.GetProperty("id").GetGuid();
        var roomType = await PostAsync("/api/room-types", new { propertyId, name = "Ev Room", code = "EVR", baseRate = 120m, totalRooms = 5 });

        var created = await _client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId,
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(9).ToString("O"),
                numberOfGuests = 2
            }
        }, JsonOptions);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        var confirmed = await _client.PostAsJsonAsync($"/api/reservations/{reservationId}/confirm", new { }, JsonOptions);
        confirmed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var invoices = await _client.GetFromJsonAsync<JsonElement>($"/api/invoices/search?reservationId={reservationId}", JsonOptions);
        return invoices.GetProperty("items")[0];
    }

    private async Task<JsonElement> PostAsync(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private sealed class Recorder(ConcurrentQueue<InvoicePaymentRecorded> received) : IDomainEventHandler<InvoicePaymentRecorded>
    {
        public Task HandleAsync(InvoicePaymentRecorded @event, CancellationToken ct = default)
        {
            received.Enqueue(@event);
            return Task.CompletedTask;
        }
    }
}
