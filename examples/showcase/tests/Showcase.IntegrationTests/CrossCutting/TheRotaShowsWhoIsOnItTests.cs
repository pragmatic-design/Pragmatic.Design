using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Accounts.Entities;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A declared join reaches an entity no navigation touches, and its columns arrive.
/// </summary>
/// <remarks>
///     <para>
///         <c>StaffAssignment.StaffId</c> is a plain <c>Guid</c> written from a token: no
///         <c>[Relation]</c>, therefore no navigation, therefore nothing for <c>[EagerLoad]</c> to
///         load and nothing for <c>[GenerateProjection]</c> to flatten across. It is the one read in
///         the Showcase that only a declared join can express, which is why the adoption lives here
///         and not on a pair that a projection already answers.
///     </para>
///     <para>
///         ⚠️ And <c>Left</c> is not decoration. Because <c>StaffId</c> is not a foreign key, an
///         assignment can name somebody who has no account — a rota written before the account
///         exists, or kept after it is gone. With <c>Inner</c> that row leaves the rota altogether;
///         with <c>Left</c> it stays, without a name. The second assertion below is that difference,
///         and it is the reason the query declares the type it does.
///     </para>
/// </remarks>
public sealed class TheRotaShowsWhoIsOnItTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheRota_CarriesTheNameOfWhoeverHasAnAccount_AndKeepsTheRowOfWhoeverDoesNot()
    {
        var role = $"Concierge-{Guid.NewGuid():N}";

        // ⚠️ Two properties, not two assignments on one: [TemporalRelation<Property>(MaxActive = 1)]
        // refuses a second active assignment for the same property, and this case is about the join,
        // not about that rule.
        var withAccount = await CreatePropertyIdAsync();
        var withoutAccount = await CreatePropertyIdAsync();

        var namedStaffId = Guid.NewGuid();
        var namelessStaffId = Guid.NewGuid();

        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<AppUser.Repository>();
            var user = AppUser.Create();
            namedStaffId = user.Id;
            user.DisplayName = "Ada Lovelace";
            user.Department = "Front desk";
            users.Add(user);
            await users.SaveChangesAsync();

            var assignments = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();
            assignments.Add(StaffAssignment.Create(namedStaffId, withAccount, role, DateTimeOffset.UtcNow.AddDays(-1)));
            assignments.Add(StaffAssignment.Create(namelessStaffId, withoutAccount, role, DateTimeOffset.UtcNow.AddDays(-1)));
            await assignments.SaveChangesAsync();
        }

        var rota = await RotaAsync(role);

        rota.Should().HaveCount(2,
            "the left join keeps the assignment whose StaffId matches no user — with an inner join "
            + "this is 1, which is the whole of what Type declares");

        rota.Single(r => r.StaffId == namedStaffId).StaffDisplayName.Should().Be("Ada Lovelace",
            "the joined entity's column reaches the result, which no navigation could have carried "
            + "because there is no navigation");

        rota.Single(r => r.StaffId == namelessStaffId).StaffDisplayName.Should().BeNull(
            "an outer join's missing row yields the type's default rather than raising");
    }

    /// <summary>
    ///     The control: the join reads the application's data, not a set of its own.
    /// </summary>
    /// <remarks>
    ///     Without it, "the name arrives" is satisfied by any string the step happened to produce. This
    ///     asserts the value is the one written through the user repository a moment earlier — so a
    ///     join reading an empty or unrelated set fails here.
    /// </remarks>
    [Fact]
    public async Task TheJoinedNameIsTheOneTheApplicationWrote()
    {
        var role = $"Porter-{Guid.NewGuid():N}";
        var propertyId = await CreatePropertyIdAsync();
        var expected = $"Grace Hopper {Guid.NewGuid():N}";

        Guid staffId;
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<AppUser.Repository>();
            var user = AppUser.Create();
            staffId = user.Id;
            user.DisplayName = expected;
            users.Add(user);
            await users.SaveChangesAsync();

            var assignments = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();
            assignments.Add(StaffAssignment.Create(staffId, propertyId, role, DateTimeOffset.UtcNow.AddDays(-1)));
            await assignments.SaveChangesAsync();
        }

        var rota = await RotaAsync(role);

        rota.Should().ContainSingle();
        rota[0].StaffDisplayName.Should().Be(expected);
    }

    /// <summary>
    ///     The rota, with the failure body in the message when it does not answer.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>EnsureSuccessStatusCode</c> throws with the status and nothing else, and this story
    ///     lost two runs to it: the 500 was the executor's own refusal, naming exactly what was
    ///     missing, and the exception hid it. A read that can fail server-side says why.
    /// </remarks>
    private async Task<List<StaffRow>> RotaAsync(string role)
    {
        var response = await GetRawAsync($"api/staff-assignments?role={role}");
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"the rota has to answer — body was: {body}");

        return System.Text.Json.JsonSerializer.Deserialize<List<StaffRow>>(body, JsonOptions)!;
    }

    /// <summary>The shape the endpoint answers with, named here so the test reads what it asserts.</summary>
    private sealed class StaffRow
    {
        public Guid StaffId { get; init; }
        public Guid PropertyId { get; init; }
        public string Role { get; init; } = "";
        public string? StaffDisplayName { get; init; }
        public string? StaffDepartment { get; init; }
    }

    private async Task<Guid> CreatePropertyIdAsync()
    {
        var created = await PostAsync<System.Text.Json.JsonElement>("/api/properties", new
        {
            code = $"RT-{Guid.NewGuid():N}"[..12],
            name = $"Rota-{Guid.NewGuid():N}"[..20],
            city = "Turin",
            country = "IT",
            starRating = 4,
        });

        return created.GetProperty("id").GetGuid();
    }
}
