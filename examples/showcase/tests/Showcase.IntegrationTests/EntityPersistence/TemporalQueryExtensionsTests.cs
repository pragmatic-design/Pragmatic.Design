using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Query;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     The temporal query extensions [TemporalRelation] generates on StaffAssignment: Active(), ActiveAt() and
///     AutoClosePrevious(), which compose onto the repository's IQueryable.
/// </summary>
/// <remarks>
///     The history scope, the constraints and the database's refusals, in <c>TemporalRelationTests</c>, run on every engine.
/// </remarks>
public class TemporalQueryExtensionsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Active query: only currently active assignments are returned
    // =========================================================================

    [Fact]
    public async Task Active_ReturnsOnlyCurrentlyActiveAssignments()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // Create an active assignment (ValidTo = null → still active)
        var active = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Manager", now.AddDays(-10));
        repository.Add(active);

        // Create an expired assignment (ValidTo in the past)
        var expired = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Receptionist", now.AddDays(-60));
        expired.ValidTo = now.AddDays(-1);
        repository.Add(expired);

        await repository.SaveChangesAsync();

        // Query active — should only return the active one
        var activeResults = await repository.Query()
            .ForProperty(propertyId)
            .Active()
            .ToListAsync();

        activeResults.Should().ContainSingle(
            "Only one assignment is currently active for this property");
        activeResults[0].Role.Should().Be("Manager");
    }

    // =========================================================================
    // ActiveAt: point-in-time query returns correct assignments
    // =========================================================================

    [Fact]
    public async Task ActiveAt_PointInTime_ReturnsCorrectAssignment()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // Assignment that was active 30 days ago but closed 10 days ago
        var pastAssignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "OldManager", now.AddDays(-60));
        pastAssignment.ValidTo = now.AddDays(-10);
        repository.Add(pastAssignment);

        // Current assignment started 5 days ago
        var currentAssignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "NewManager", now.AddDays(-5));
        repository.Add(currentAssignment);

        await repository.SaveChangesAsync();

        // ActiveAt 20 days ago — should return the old manager (was active at that time)
        // Use Raw strategy to bypass the auto-applied TemporalFilter (which only shows currently active),
        // then apply ActiveAt explicitly for point-in-time filtering
        var twentyDaysAgo = now.AddDays(-20);
        var pastResults = await repository.Query(QueryStrategy.Raw)
            .ForProperty(propertyId)
            .ActiveAt(twentyDaysAgo)
            .ToListAsync();

        pastResults.Should().ContainSingle();
        pastResults[0].Role.Should().Be("OldManager",
            "ActiveAt(20 days ago) should return the assignment that was active at that time");

        // ActiveAt now — should return the new manager
        var nowResults = await repository.Query(QueryStrategy.Raw)
            .ForProperty(propertyId)
            .ActiveAt(now)
            .ToListAsync();

        nowResults.Should().ContainSingle();
        nowResults[0].Role.Should().Be("NewManager",
            "ActiveAt(now) should return the currently active assignment");
    }

    // =========================================================================
    // AutoClosePrevious: closes active record when new one starts
    // =========================================================================

    [Fact]
    public async Task AutoClosePrevious_ClosesActiveRecordForSameProperty()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // Active assignment
        var active = StaffAssignment.Create(Guid.NewGuid(), propertyId, "OldManager", now.AddDays(-30));
        repository.Add(active);
        await repository.SaveChangesAsync();

        // AutoClose the previous active assignment (offset ensures ValidTo is clearly in the past)
        var closedAt = now.AddSeconds(-1);
        var closed = StaffAssignment.AutoClosePrevious(
            repository.Query().ForProperty(propertyId),
            closedAt,
            propertyId);

        closed.Should().ContainSingle("There should be one active record to close");
        closed[0].ValidTo.Should().Be(closedAt,
            "AutoClosePrevious should set ValidTo to the specified timestamp");

        await repository.SaveChangesAsync();

        // Now add a new assignment — should be the only active one
        var newAssignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "NewManager", closedAt);
        repository.Add(newAssignment);
        await repository.SaveChangesAsync();

        var activeNow = await repository.Query()
            .ForProperty(propertyId)
            .Active()
            .ToListAsync();

        activeNow.Should().ContainSingle(
            "After AutoClose + new insert, only the new assignment should be active");
        activeNow[0].Role.Should().Be("NewManager");
    }

    /// <summary>
    ///     <c>Active()</c> answers about the instant it is given, not about the moment the test runs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The generated temporal code read <c>DateTimeOffset.UtcNow</c> inline, so "what is
    ///         active" could only ever be measured against the wall clock: to see a row expire you had
    ///         to wait for it. The framework forbids exactly this to its users — <c>PRAG0900</c> says to
    ///         inject <c>IClock</c> or <c>TimeProvider</c> — and a generated file is not analysed, so
    ///         nothing said it.
    ///     </para>
    ///     <para>
    ///         The row is active on the wall clock, which is the zero: both refusals below are the clock
    ///         moving, not the row being wrong. And both directions are asserted, because a query that
    ///         returned nothing whatever it was asked would satisfy only one of them.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task Active_AnswersAboutTheInstantItIsGiven_NotTheWallClock()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // Valid from yesterday to a week out: active now, and only now.
        var assignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Manager", now.AddDays(-1));
        assignment.ValidTo = now.AddDays(7);
        repository.Add(assignment);
        await repository.SaveChangesAsync();

        var today = await repository.Query().ForProperty(propertyId).Active().ToListAsync();
        today.Should().ContainSingle("the row is active at the wall clock — this is the zero");

        var aMonthOn = await repository.Query().ForProperty(propertyId)
            .Active(new FixedClock(now.AddDays(30))).ToListAsync();
        aMonthOn.Should().BeEmpty(
            "ValidTo is a week out and the clock says a month has passed — measured, not waited for");

        var aMonthBefore = await repository.Query().ForProperty(propertyId)
            .Active(new FixedClock(now.AddDays(-30))).ToListAsync();
        aMonthBefore.Should().BeEmpty("and ValidFrom is yesterday, so a month ago it had not begun");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>A clock stopped at one instant, so "when" is an input instead of the wall clock.</summary>
    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    /// <summary>
    /// Creates a Property via HTTP and returns its ID (needed as FK for StaffAssignment).
    /// </summary>
    private async Task<Guid> CreatePropertyIdAsync()
    {
        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"TR-{Guid.NewGuid():N}"[..12],
            name = $"TempRel-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        return prop.GetProperty("id").GetGuid();
    }
}

/// <summary>
/// Extension to make EF Core ToListAsync available in test without importing full namespace.
/// </summary>
internal static class TemporalRelationTestExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> queryable)
    {
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(queryable);
    }
}
