using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     <c>[FilterMode(Admin)]</c>: the audit reads across the data scopes, and the ordinary
///     search still does not.
/// </summary>
/// <remarks>
///     <para>
///         <c>Invoice</c> is <c>[HasAccessScopes]</c>, and <c>EurInvoiceScopeRule</c> puts
///         <c>scope:billing-eu</c> on a EUR invoice. A caller whose only scope names the other bucket
///         is answered nothing by <c>SearchInvoicesQuery</c> — which is what
///         <see cref="ADataScopeRuleDecidesWhoSeesTheInvoiceTests" /> pins.
///     </para>
///     <para>
///         ⚠️ <b>The two assertions are one test on purpose.</b> "The audit sees it" is satisfied by a
///         scope filter that stopped filtering, and that is precisely the defect the scope rules once
///         had — every row with an empty scope list and the filter false for everyone. The
///         same caller, the same invoice, two routes: one lifts the scopes because it declares that it
///         does, and the other does not.
///     </para>
/// </remarks>
public class TheAuditReadsAcrossTheScopesTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheAudit_SeesAnInvoiceTheCallersScopesDoNotReach()
    {
        var reservationId = await AEuroInvoiceAsync();

        using var auditor = AnAuditorWithNoScopeOfTheirOwn();

        (await CountAsync(auditor, "search", reservationId)).Should().Be(0,
            "the ordinary search answers what the caller's scopes reach, and this one holds none");

        (await CountAsync(auditor, "audit", reservationId)).Should().Be(1,
            "the audit declares that it reads across the scopes, and the permission is the gate");
    }

    /// <summary>
    ///     The control: the lift is the operation's, not the caller's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "[FilterMode] lifted the scopes" is indistinguishable from "this caller was
    ///     allowed to see everything anyway" — which is what the generated
    ///     <c>billing.invoice.view-all</c> does, and is the thing this declaration is not. The auditor
    ///     above holds no such permission, and here the same caller is refused the audit route
    ///     entirely once the audit permission goes.
    /// </remarks>
    [Fact]
    public async Task WithoutTheAuditPermission_TheRouteIsRefused()
    {
        var reservationId = await AEuroInvoiceAsync();

        using var reader = ClientWithScopes("billing.invoice.read", dataScope: null);

        var response = await reader.GetAsync($"/api/invoices/audit?reservationId={reservationId}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    private static async Task<int> CountAsync(HttpClient client, string route, Guid reservationId)
    {
        var response = await client.GetAsync($"/api/invoices/{route}?reservationId={reservationId}");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("items").GetArrayLength();
    }

    /// <summary>An auditor: allowed to run the audit, and holding no data scope at all.</summary>
    private HttpClient AnAuditorWithNoScopeOfTheirOwn()
        => ClientWithScopes("billing.invoice.read,billing.invoice.audit", dataScope: null);

    private HttpClient ClientWithScopes(string permissions, string? dataScope)
    {
        var client = CreateClientAs($"au-{Guid.NewGuid():N}"[..12], "Audit User");
        client.DefaultRequestHeaders.Remove("X-User-Permissions");
        client.DefaultRequestHeaders.Add("X-User-Permissions", permissions);

        if (dataScope is not null)
            client.DefaultRequestHeaders.Add("X-User-Scopes", dataScope);

        return client;
    }

    /// <summary>A confirmed reservation, which bills in EUR and so is scoped to the EUR bucket.</summary>
    private async Task<Guid> AEuroInvoiceAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Audit",
            lastName = "Scope",
            email = $"audit.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AU-{Guid.NewGuid():N}"[..12],
            name = $"AuditProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "AU Room",
            code = $"AU{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 5
        });

        var created = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 1
            }
        }, JsonOptions);

        created.EnsureSuccessStatusCode();

        // A bare Guid on the wire, not an envelope: this route answers with the id itself.
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        (await Client.PostAsJsonAsync($"/api/reservations/{reservationId}/confirm", new { }, JsonOptions))
            .EnsureSuccessStatusCode();

        return reservationId;
    }
}
