using Pragmatic.Persistence.Query.Filters;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Query;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests [TemporalRelation] behavior on StaffAssignment via DI-resolved repository.
///     StaffAssignment has no HTTP endpoints, so tests resolve the repository
///     from the host's IServiceProvider and test persistence directly.
///     Covers matrix features:
///       - [TemporalRelation&lt;Property&gt;] with MaxActive = 1
///       - Active() / ActiveAt() / IncludeHistory() temporal query extensions
///       - ValidateTemporalConstraints (scoped per Property)
///       - AutoClosePrevious
///       - TemporalFilter (default query only returns active records)
/// </summary>
public class TemporalRelationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
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
    // IncludeHistory: returns all records including expired
    // =========================================================================

    [Fact]
    public async Task IncludeHistory_ReturnsAllRecordsIncludingExpired()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // Active assignment
        var active = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Current", now.AddDays(-5));
        repository.Add(active);

        // Expired assignment
        var expired = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Historical", now.AddDays(-90));
        expired.ValidTo = now.AddDays(-30);
        repository.Add(expired);

        await repository.SaveChangesAsync();

        // ⚠️ The ordinary Query(), not Query(QueryStrategy.Raw): Raw bypasses the temporal filter on
        // its own, so a test built on it proves nothing about IncludeHistory — and Raw drops tenant
        // isolation and soft-delete as well. IncludeHistory is a scope that lifts exactly one filter,
        // so the assertion is about the method.
        var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();

        using (StaffAssignment.IncludeHistory(filters))
        {
            var allResults = await repository.Query()
                .ForProperty(propertyId)
                .ToListAsync();

            allResults.Should().HaveCountGreaterOrEqualTo(2,
                "IncludeHistory should return both active and expired records");
        }

        // The control: outside the scope the closed stretch is hidden
        // again, which is what makes the assertion above about IncludeHistory and not about Raw.
        var activeOnly = await repository.Query().ForProperty(propertyId).ToListAsync();

        activeOnly.Should().ContainSingle(
            "the temporal filter is back on, so only the open stretch is visible");
    }

    // =========================================================================
    // MaxActive = 1 on the write path that does not go through a mutation
    // =========================================================================

    /// <summary>⚠️ The database refuses a second open stretch, whoever is writing.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>MaxActive = 1</c> reads as an invariant of the entity, so the generated mutation invoker
    ///         cannot be the only place that enforces it. An action writing the same entity through the
    ///         same repository never reaches that check — <b>measured on an application</b>: two
    ///         overlapping open stretches written with a <c>200</c>, where a mutation answers <c>409</c>.
    ///     </para>
    ///     <para>
    ///         It is a partial unique index — <c>UNIQUE(PropertyId) WHERE ValidTo IS NULL</c> — so
    ///         the constraint holds on every write path there is, including SQL nobody generated. This
    ///         test writes through the repository, which is the path the pipeline check does not cover,
    ///         and the write fails.
    ///     </para>
    ///     <para>
    ///         The test beside it (<c>ValidateTemporalConstraints_MaxActiveExceeded_ReturnsError</c>)
    ///         passes too: the pipeline check runs first and gives a domain error rather than a
    ///         constraint violation. The two are layers, not alternatives — one explains, the other
    ///         guarantees.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ASecondOpenStretch_IsRefusedByTheDatabase_EvenOutsideAMutation()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // The zero: one open stretch is fine, and this is the write path under test.
        repository.Add(StaffAssignment.Create(Guid.NewGuid(), propertyId, "First", now.AddDays(-10)));
        await repository.SaveChangesAsync();

        // A second one for the same property, left open, through the same repository — no mutation, so
        // no ValidateTemporalConstraints anywhere on this path.
        repository.Add(StaffAssignment.Create(Guid.NewGuid(), propertyId, "Second", now.AddDays(-1)));

        var write = async () => await repository.SaveChangesAsync();

        await write.Should().ThrowAsync<Exception>(
            "the partial unique index holds on every write path, not only through a mutation");

        // The control: a CLOSED second stretch is not a second open one, so the index admits it. Without
        // this the assertion above would also hold on an index that refused every second row.
        using var second = Services.CreateScope();
        var other = second.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var closed = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Closed", now.AddDays(-90));
        closed.ValidTo = now.AddDays(-60);
        other.Add(closed);

        await other.SaveChangesAsync();
    }

    /// <summary>⚠️ A stretch that ends before it begins is refused by the database.</summary>
    /// <remarks>
    ///     <para>
    ///         Not a hypothetical shape: <c>AutoClosePrevious</c> closes everything whose <c>ValidTo</c>
    ///         is null or later than the instant being written, and for an instant in the past that
    ///         includes stretches which <em>begin</em> after it. A handover recorded late — ordinary
    ///         office work — produced exactly this row.
    ///     </para>
    ///     <para>
    ///         An inverted stretch matches no <c>ActiveAt</c> window at all, so the person who genuinely
    ///         held the relation during those weeks disappears from the history with nothing to show it.
    ///         <c>CK_StaffAssignments_ValidRange</c> is what makes it impossible to write.
    ///     </para>
    ///     <para>
    ///         Ⓘ A check rather than an index because it constrains one row rather than the relationship
    ///         between rows — and so, unlike the partial unique index beside it, it is evaluated per row
    ///         and does not care in which order EF emits its statements.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AStretchEndingBeforeItBegins_IsRefusedByTheDatabase()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        var inverted = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Impossible", now.AddDays(-5));
        inverted.ValidTo = now.AddDays(-30);
        repository.Add(inverted);

        var write = async () => await repository.SaveChangesAsync();

        await write.Should().ThrowAsync<Exception>(
            "an interval cannot end before it starts, and the database is where that is enforced");

        // The control: the same row with its dates the right way round is accepted, so the refusal is
        // the invariant and not something else about the write.
        using var second = Services.CreateScope();
        var other = second.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var ordered = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Ordered", now.AddDays(-30));
        ordered.ValidTo = now.AddDays(-5);
        other.Add(ordered);

        await other.SaveChangesAsync();
    }

    // =========================================================================
    // ValidateTemporalConstraints: MaxActive = 1 prevents second active
    // =========================================================================

    [Fact]
    public async Task ValidateTemporalConstraints_MaxActiveExceeded_ReturnsError()
    {
        var propertyId = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // First active assignment
        var first = StaffAssignment.Create(Guid.NewGuid(), propertyId, "FirstManager", now.AddDays(-10));
        repository.Add(first);
        await repository.SaveChangesAsync();

        // Try to add a second active assignment for the SAME property
        var second = StaffAssignment.Create(Guid.NewGuid(), propertyId, "SecondManager", now);
        var existing = repository.Query().ForProperty(propertyId);
        var error = second.ValidateTemporalConstraints(existing);

        error.Should().NotBeNull(
            "MaxActive=1 should be violated when adding a second active assignment for the same property");
        error!.ViolationType.Should().Be(Pragmatic.Persistence.Entity.TemporalViolationType.MaxActiveExceeded);
        error.MaxActive.Should().Be(1);
    }

    // =========================================================================
    // ValidateTemporalConstraints: different property → no conflict
    // =========================================================================

    [Fact]
    public async Task ValidateTemporalConstraints_DifferentProperty_NoConflict()
    {
        var propertyIdA = await CreatePropertyIdAsync();
        var propertyIdB = await CreatePropertyIdAsync();

        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<StaffAssignment.Repository>();

        var now = DateTimeOffset.UtcNow;

        // Active assignment for property A
        var assignmentA = StaffAssignment.Create(Guid.NewGuid(), propertyIdA, "Manager", now.AddDays(-10));
        repository.Add(assignmentA);
        await repository.SaveChangesAsync();

        // New assignment for property B — should NOT conflict
        var assignmentB = StaffAssignment.Create(Guid.NewGuid(), propertyIdB, "Manager", now);
        var existing = repository.Query();
        var error = assignmentB.ValidateTemporalConstraints(existing);

        error.Should().BeNull(
            "MaxActive is scoped per Property — different properties should not conflict");
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

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Creates a Property via HTTP and returns its ID (needed as FK for StaffAssignment).
    /// </summary>
    /// <summary>A clock stopped at one instant, so "when" is an input instead of the wall clock.</summary>
    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
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
