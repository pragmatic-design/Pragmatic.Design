using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     <c>[GenerateTimeline]</c> on <c>StaffAssignment</c>: who held the role, in which
///     order, and where the handovers were.
/// </summary>
/// <remarks>
///     <para>
///         The attribute generates a LAG/LEAD CTE — each validity period with the end of the one before
///         and the start of the one after — partitioned by the property, so one hotel's handovers do not
///         bleed into another's. Written by hand it is a window function and a raw query; declared, the
///         column names come from the EF model instead of from a guess.
///     </para>
///     <para>
///         ⚠️ <b>Asserted on the rows, never on the method existing.</b> The temporal family already has
///         one of these: <c>IncludeHistory()</c> was a no-op behind a green suite for as long as nobody
///         counted what it returned. A timeline with no example is the same configuration, which is why
///         this story asked for the timeline to be <em>read back</em>.
///     </para>
///     <para>
///         The entries carry no key — only the four instants — so they are matched by the validity
///         instants this test wrote, which are its own. The suite shares a database and the table holds
///         other classes' assignments.
///     </para>
/// </remarks>
public class TheTimelineOfARoleTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheHandover_IsInTheTimeline()
    {
        var (first, second) = await ARoleHandedOverAsync();

        var timeline = await TimelineAsync();

        var opening = timeline.Should().ContainSingle(e => e.ValidFrom == first).Which;
        opening.ValidTo.Should().Be(second, "the first holder's period was closed when the second began");
        opening.NextValidFrom.Should().Be(second,
            "which is what the CTE is for: the period after this one, without a second query");

        var current = timeline.Should().ContainSingle(e => e.ValidFrom == second).Which;
        current.PreviousValidTo.Should().Be(second, "the handover is the same instant from both sides");
        current.NextValidFrom.Should().BeNull("nobody has taken it over yet");
    }

    /// <summary>
    ///     The control: the periods of another property are not in this one's chain.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "the timeline links the periods" is satisfied by a CTE with no
    ///     <c>PARTITION BY</c> — which links every hotel's handover to whichever came next in the whole
    ///     table, and reads as a working timeline until somebody has two properties.
    /// </remarks>
    [Fact]
    public async Task TheChainDoesNotCrossProperties()
    {
        var (first, second) = await ARoleHandedOverAsync();
        var elsewhere = await ARoleStartedElsewhereAsync(between: first.AddHours(1));

        var timeline = await TimelineAsync();

        timeline.Should().ContainSingle(e => e.ValidFrom == first)
            .Which.NextValidFrom.Should().Be(second,
                "the next period of THIS property, not the one that started at another hotel in between");
        timeline.Should().ContainSingle(e => e.ValidFrom == elsewhere)
            .Which.PreviousValidTo.Should().BeNull("it is the first period of its own property");
    }

    /// <summary>One property, one role, two holders: the first closed when the second began.</summary>
    private async Task<(DateTimeOffset First, DateTimeOffset Second)> ARoleHandedOverAsync()
    {
        // Instants of this test's own, so its rows can be found in a table the suite shares. Seconds
        // are truncated because the column keeps microseconds and the comparison has to be exact.
        var first = new DateTimeOffset(2019, 3, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Random.Shared.Next(1, 500_000));
        var second = first.AddDays(30);

        var propertyId = await APropertyAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var opening = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Concierge", first);
        opening.ValidTo = second;
        repository.Add(opening);
        repository.Add(StaffAssignment.Create(Guid.NewGuid(), propertyId, "Concierge", second));

        await repository.SaveChangesAsync();

        return (first, second);
    }

    /// <summary>A property of this test's own, so its assignments are alone in their partition.</summary>
    private async Task<Guid> APropertyAsync()
    {
        var created = await PostAsync<System.Text.Json.JsonElement>("/api/properties", new
        {
            code = $"TL-{Guid.NewGuid():N}"[..12],
            name = $"Timeline-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>A period at another property, starting in the middle of the one above.</summary>
    private async Task<DateTimeOffset> ARoleStartedElsewhereAsync(DateTimeOffset between)
    {
        var propertyId = await APropertyAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        repository.Add(StaffAssignment.Create(Guid.NewGuid(), propertyId, "Concierge", between));
        await repository.SaveChangesAsync();

        return between;
    }

    /// <remarks>
    ///     ⚠️ The entry type is <b>nested</b> in the generated extensions class, so a caller names it
    ///     <c>StaffAssignmentTimelineExtensions.StaffAssignmentTimelineEntry</c>. Nothing says so —
    ///     inside the generated file the return type is written unqualified and compiles.
    /// </remarks>
    private async Task<List<StaffAssignmentTimelineExtensions.StaffAssignmentTimelineEntry>> TimelineAsync()
    {
        using var scope = Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<BookingDbContext>()
            .GetTimeline()
            .ToListAsync();
    }
}
