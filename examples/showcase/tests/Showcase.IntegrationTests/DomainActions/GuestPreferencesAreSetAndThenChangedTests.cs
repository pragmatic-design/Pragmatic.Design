using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     <c>PUT /api/guests/{guestId}/preferences</c> is an upsert: the first call creates the guest's preferences, the second changes the same
///     row.
/// </summary>
/// <remarks>
///     The route's only tests were its 403 and its 404, which never reach the body. The second call is the half that matters: it finds the row by
///     <c>GuestId</c> and changes it through the generated setters, and the unit of work saves it only if that read tracked the row.
/// </remarks>
public class GuestPreferencesAreSetAndThenChangedTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheSecondPut_ChangesTheRowTheFirstCreated()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Pref",
            lastName = "Guest",
            email = $"pref.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        // The caller that created the guest: another user would not see it, and the load would answer 404 before the body runs.
        var first = await PutAsync($"/api/guests/{guestId}/preferences",
            new { preferredRoomType = "Suite", prefersHighFloor = true });
        first.StatusCode.Should().Be(HttpStatusCode.OK, "{0}", await first.Content.ReadAsStringAsync());
        var firstId = await first.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        var second = await PutAsync($"/api/guests/{guestId}/preferences",
            new { preferredRoomType = "Double", prefersHighFloor = false, dietaryRequirements = "Vegetarian" });
        second.StatusCode.Should().Be(HttpStatusCode.OK, "{0}", await second.Content.ReadAsStringAsync());
        var secondId = await second.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        secondId.Should().Be(firstId, "the second call finds the guest's preferences and changes them, rather than adding a second row");

        // Read back from the database: the answer of the second call is the id it found, which says nothing about whether the change was saved.
        using var scope = Services.CreateScope();
        var rows = await scope.ServiceProvider
            .GetRequiredService<IReadRepository<GuestPreferences>>()
            .FindAsync(Spec<GuestPreferences>.Where(p => p.GuestId == guestId));

        var stored = rows.Should().ContainSingle("one guest, one row of preferences").Which;
        stored.PreferredRoomType.Should().Be("Double");
        stored.PrefersHighFloor.Should().BeFalse();
        stored.DietaryRequirements.Should().Be("Vegetarian");
    }
}
