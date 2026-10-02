using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Email.Testing;
using Pragmatic.Email.Transport;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     End-to-end notification delivery: confirming a reservation raises ReservationConfirmed, whose
///     handler enqueues a notification; the background worker then resolves the guest, routes to the
///     e-mail channel and hands the message to Pragmatic.Email.
/// </summary>
/// <remarks>
///     This path was never covered. The handler addresses the guest with
///     <c>NotificationRecipient.User(...)</c>, which the built-in resolver cannot turn into an address,
///     so before <c>GuestRecipientResolver</c> was registered the notification silently resolved to
///     nothing and no e-mail was ever produced.
///     The SMTP transport is swapped for the in-memory one so the assertion needs no mail server.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class NotificationDeliveryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ShowcaseWebFactory _baseFactory = null!;
    private WebApplicationFactoryWrapper _factory = null!;
    private HttpClient _client = null!;
    private InMemoryTransport _transport = null!;

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class WebApplicationFactoryWrapper(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> inner) : IAsyncDisposable
    {
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Inner { get; } = inner;

        public async ValueTask DisposeAsync() => await Inner.DisposeAsync().ConfigureAwait(false);
    }

    public Task InitializeAsync()
    {
        _transport = new InMemoryTransport();

        _baseFactory = new ShowcaseWebFactory(fixture);
        _factory = new WebApplicationFactoryWrapper(_baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(_transport);
            })));

        _client = _factory.Inner.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "notification-test-user");
        _client.DefaultRequestHeaders.Add("X-User-Name", "Notification Test");
        _client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*,billing.invoice.create");

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
        await _baseFactory.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ConfirmingAReservation_SendsTheGuestAConfirmationEmail()
    {
        var guestEmail = $"guest.{Guid.NewGuid():N}@example.com";
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(guestEmail).ConfigureAwait(true);
        var reservationId = await CreateReservationAsync(guestId, propertyId, roomTypeId).ConfigureAwait(true);

        var confirm = await _client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/confirm", new { }, JsonOptions).ConfigureAwait(true);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Delivery is asynchronous (EnqueueAsync → background worker), so poll rather than assert once.
        var delivered = await WaitForEmailAsync(guestEmail, TimeSpan.FromSeconds(30)).ConfigureAwait(true);

        delivered.Should().BeTrue("the reservation confirmation must reach the guest's address");

        var sent = _transport.SentWhere(m => m.To.Any(t => t.Address == guestEmail)).Single();
        sent.Message.Subject.Should().StartWith("Reservation confirmed");
        sent.Message.TextBody.Should().Contain("Dear Notify Guest, your reservation has been confirmed.");
        sent.Message.HtmlBody.Should().Contain("Reservation confirmed");
    }

    /// <summary>
    ///     A guest who reads Italian gets the confirmation in Italian, from the same template.
    /// </summary>
    /// <remarks>
    ///     The request confirming the booking is made in English — the member of staff's language — so
    ///     the assertion means the mail follows the guest, not the caller. The template owns the subject
    ///     too, which is why it is translated along with the body.
    /// </remarks>
    [Fact]
    public async Task AnItalianGuest_IsWrittenToInItalian()
    {
        var guestEmail = $"ospite.{Guid.NewGuid():N}@example.com";
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(guestEmail, "it").ConfigureAwait(true);
        var reservationId = await CreateReservationAsync(guestId, propertyId, roomTypeId).ConfigureAwait(true);

        var confirm = await _client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/confirm", new { }, JsonOptions).ConfigureAwait(true);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await WaitForEmailAsync(guestEmail, TimeSpan.FromSeconds(30)).ConfigureAwait(true)).Should().BeTrue();

        var sent = _transport.SentWhere(m => m.To.Any(t => t.Address == guestEmail)).Single();
        sent.Message.Subject.Should().StartWith("Prenotazione confermata");
        sent.Message.TextBody.Should().Contain("Gentile Notify Guest, la tua prenotazione è confermata.");
        sent.Message.HtmlBody.Should().Contain("Arrivo").And.NotContain("Check-in");
    }

    private async Task<bool> WaitForEmailAsync(string address, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_transport.HasSentTo(address))
                return true;

            await Task.Delay(250).ConfigureAwait(true);
        }

        return false;
    }

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(
        string guestEmail, string preferredLanguage = "en")
    {
        var guestResponse = await _client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "Notify",
            lastName = "Guest",
            email = guestEmail,
            preferredLanguage,
        }, JsonOptions).ConfigureAwait(true);
        guestResponse.EnsureSuccessStatusCode();
        var guest = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(true);

        var propertyResponse = await _client.PostAsJsonAsync("/api/properties", new
        {
            code = $"NT-{Guid.NewGuid():N}"[..12],
            name = $"NotifyProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 4,
        }, JsonOptions).ConfigureAwait(true);
        propertyResponse.EnsureSuccessStatusCode();
        var property = await propertyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(true);
        var propertyId = property.GetProperty("id").GetGuid();

        var roomTypeResponse = await _client.PostAsJsonAsync("/api/room-types", new
        {
            propertyId,
            name = "Notify Room",
            code = "NTR",
            baseRate = 120m,
            totalRooms = 4,
        }, JsonOptions).ConfigureAwait(true);
        roomTypeResponse.EnsureSuccessStatusCode();
        var roomType = await roomTypeResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(true);

        return (guest.GetProperty("id").GetGuid(), propertyId, roomType.GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateReservationAsync(Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var response = await _client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2,
            },
        }, JsonOptions).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions).ConfigureAwait(true);
    }
}
