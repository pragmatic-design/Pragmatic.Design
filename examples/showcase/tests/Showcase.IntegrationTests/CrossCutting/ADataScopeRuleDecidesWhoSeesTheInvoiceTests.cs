using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A <c>DataScopeRule</c> puts a scope on the row, and that scope decides who reads it.
/// </summary>
/// <remarks>
///     <para>
///         The Showcase registers two rules on <c>Invoice</c> — <c>EurInvoiceScopeRule</c> and
///         <c>UsdInvoiceScopeRule</c>, both <c>ScopeStrategy.Materialized</c> — and once
///         **neither had ever been evaluated against a row**: <c>IScopeMaterializer</c> was registered
///         by the host and called by nothing but its own unit tests. Every invoice reached the database
///         with an empty scope list, so the generated filter was false for everyone and only
///         <c>billing.invoice.view-all</c> could see anything.
///     </para>
///     <para>
///         The scope reaches the caller through a <c>data-scope</c> claim: <c>X-User-Scopes</c> on the
///         request becomes that claim, and <c>DefaultUserScopeResolver</c> expands it into
///         <c>scope:{name}</c> — the same spelling <c>ScopeIdentifiers</c> writes onto the row.
///     </para>
/// </remarks>
public class ADataScopeRuleDecidesWhoSeesTheInvoiceTests(PostgresFixture fixture)
    : IntegrationTestBase(fixture)
{
    /// <summary>The EUR scope reaches a EUR invoice, and the USD scope does not.</summary>
    [Fact]
    public async Task TheEuroScope_ReachesAEuroInvoice_AndTheDollarScopeDoesNot()
    {
        var reservationId = await CreateAndConfirmReservationAsync();

        using var euClient = ClientWithDataScope("billing-eu");
        using var usdClient = ClientWithDataScope("billing-usd");

        (await SeesAsync(euClient, reservationId)).Should().BeTrue(
            "the invoice is in EUR, the rule put scope:billing-eu on it, and this caller holds it");

        // The control: without it "the EUR caller sees it" is satisfied by a filter that stopped
        // filtering, or by a materialization that puts every scope on every row.
        (await SeesAsync(usdClient, reservationId)).Should().BeFalse(
            "the same invoice, a caller whose only scope names the other bucket");
    }

    /// <summary>
    ///     The second control: the creator's own scope is still there beside the rule's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the assertion that catches the ordering being reversed. The creator's stamp is
    ///     written only when <c>AccessScopes</c> is empty, so materializing first would fill the list and
    ///     the stamp would never fire again — in silence, with the case above still
    ///     green. The two only pass together.
    /// </remarks>
    [Fact]
    public async Task TheRulesScope_DoesNotDisplaceTheCreatorsOwn()
    {
        using var creator = CreateClientAs("scope-rule-creator", "Scope Rule Creator");
        var reservationId = await CreateAndConfirmReservationAsAsync(creator);

        using var narrowed = CreateClientAs("scope-rule-creator", "Scope Rule Creator");
        narrowed.DefaultRequestHeaders.Remove("X-User-Permissions");
        narrowed.DefaultRequestHeaders.Add("X-User-Permissions",
            "billing.invoice.read,billing.invoice.create");

        (await SeesAsync(narrowed, reservationId)).Should().BeTrue(
            "the row still carries user:scope-rule-creator, and no view-all was needed to read it");
    }

    private HttpClient ClientWithDataScope(string dataScope)
    {
        var client = CreateClientAs($"ds-{Guid.NewGuid():N}"[..12], "Data Scope User");
        client.DefaultRequestHeaders.Remove("X-User-Permissions");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "billing.invoice.read");
        client.DefaultRequestHeaders.Add("X-User-Scopes", dataScope);
        return client;
    }

    private static async Task<bool> SeesAsync(HttpClient client, Guid reservationId)
    {
        var response = await client.GetAsync($"/api/invoices/search?reservationId={reservationId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("items").GetArrayLength() > 0;
    }

    private async Task<Guid> CreateAndConfirmReservationAsync()
        => await CreateAndConfirmReservationAsAsync(Client);

    private async Task<Guid> CreateAndConfirmReservationAsAsync(HttpClient client)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Rule",
            lastName = "Scope",
            email = $"rule.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"DS-{Guid.NewGuid():N}"[..12],
            name = $"RuleProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "DS Room",
            code = $"DS{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 5
        });

        var created = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
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

        var confirmed = await client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/confirm", new { }, JsonOptions);
        confirmed.EnsureSuccessStatusCode();

        return reservationId;
    }
}
