using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     <c>[StartsDelegation]</c>: inside the scope the pipeline holds, the session
///     <b>is</b> the subject, so an owned row written there belongs to them and not to the caller.
/// </summary>
/// <remarks>
///     <para>
///         The row is attributed at <c>SaveChanges</c>, which the invoker runs after the body
///         returns, so the scope lives on <c>BeginInvocationScope</c>, which the pipeline holds across
///         the save. A scope that closed with the body would make this case measure the defect
///         instead: the guest would come back owned by the front desk, who could then read it while
///         the subject got a 404.
///     </para>
///     <para>
///         <c>Guest</c> is <c>[HasOwner]</c> and <c>[HasAccessScopes]</c>, and the row filter is an
///         OR of the two. Nobody here holds a scope, so visibility is ownership alone — which makes
///         "who can read it afterwards" a complete answer to "who owns it". The column is asserted
///         too, because a 404 has more than one cause and this case should name the one it means.
///     </para>
///     <para>
///         ⚠️ The pair is the case. The control is the ordinary <c>POST /api/guests</c> by the
///         <b>same</b> front desk, which stays readable by the front desk. Without it, "the caller
///         cannot read it" would be satisfied by a row nobody owns, or by a filter that refuses
///         everything.
///     </para>
/// </remarks>
public class WhoTheRowBelongsToWhenSomebodyElseTypedItTests(PostgresFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ARowWrittenUnderDelegation_BelongsToTheSubject_NotToTheCallerWhoTypedIt()
    {
        var frontDesk = $"front-desk-{Guid.NewGuid():N}";
        var subject = $"subject-{Guid.NewGuid():N}";

        using var deskClient = Restricted(frontDesk, "Front Desk");
        using var subjectClient = Restricted(subject, "The Subject");

        var created = await PostWithClientAsync(deskClient, "/api/guests/on-behalf-of", new
        {
            forUserId = subject,
            firstName = "Delegated",
            lastName = "Registration",
            email = $"deleg.{Guid.NewGuid():N}@test.com",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            "the caller's own permission is what opens the door — "
            + await created.Content.ReadAsStringAsync());

        var guestId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetGuid();

        var owner = await StoredOwnerAsync(guestId);
        owner.Should().Be(subject, $"owner was '{owner}'");

        (await subjectClient.GetAsync($"/api/guests/{guestId}")).StatusCode
            .Should().Be(HttpStatusCode.OK,
                "the row was written while the session was the subject, so it is theirs");

        (await deskClient.GetAsync($"/api/guests/{guestId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound,
                "and it is not the front desk's, although the front desk is who typed it");
    }

    /// <summary>
    ///     The control: the same caller, the ordinary route, no delegation — the row is the caller's.
    /// </summary>
    [Fact]
    public async Task TheSameCallerWritingDirectly_KeepsTheRow()
    {
        var frontDesk = $"front-desk-{Guid.NewGuid():N}";

        using var deskClient = Restricted(frontDesk, "Front Desk");

        var created = await PostWithClientAsync(deskClient, "/api/guests", new
        {
            firstName = "Direct",
            lastName = "Registration",
            email = $"direct.{Guid.NewGuid():N}@test.com",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());

        var guestId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        (await StoredOwnerAsync(guestId)).Should().Be(frontDesk,
            "without a delegation the owner is the caller, which is what makes the subject's "
            + "ownership above mean something");

        (await deskClient.GetAsync($"/api/guests/{guestId}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> StoredOwnerAsync(Guid guestId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BookingBoundary));

        return await db.Set<Guest>().IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.PersistenceId == guestId)
            .Select(g => g.OwnerId)
            .FirstAsync();
    }

    /// <summary>
    ///     A caller who can write a guest and read one back, and <b>cannot</b> bypass the row filter.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>CreateClientAs</c> would carry <c>booking.*</c>, and with it <c>booking.*.view-all</c>:
    ///     every client would see every row and the 404 here would be impossible. Measured — the
    ///     first run of this case failed on exactly that, with the front desk reading a row it did
    ///     not own.
    /// </remarks>
    private HttpClient Restricted(string userId, string name)
        => CreateClientAsWithPermissions(
            userId, name, "booking.guest.create", "booking.guest.read");
}
