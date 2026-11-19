using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Showcase.Booking;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests [TemporalRelation] behavior on StaffAssignment through the boundary's repository.
///     StaffAssignment has no HTTP endpoints for these cases, so tests resolve the repository
///     from the host's IServiceProvider and test persistence directly.
///     Covers matrix features:
///       - [TemporalRelation&lt;Property&gt;] with MaxActive = 1
///       - TemporalFilter (default read only returns active records) and IncludeHistory()
///       - ValidateTemporalConstraintsAsync (scoped per Property)
///       - the database's refusals: one open stretch per property, no stretch ending before it begins
///     The IQueryable extensions (Active(), ActiveAt(), AutoClosePrevious()) are in TemporalQueryExtensionsTests.
/// </summary>
public class TemporalRelationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // IncludeHistory: returns all records including expired
    // =========================================================================

    [Fact]
    public async Task IncludeHistory_ReturnsAllRecordsIncludingExpired()
    {
        var propertyId = await CreatePropertyIdAsync();

        var now = DateTimeOffset.UtcNow;

        // Active assignment
        var active = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Current", now.AddDays(-5));

        // Expired assignment
        var expired = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Historical", now.AddDays(-90));
        expired.ValidTo = now.AddDays(-30);

        await WriteAsync(active, expired);

        // ⚠️ The ordinary read, not one with every filter lifted: lifting them all bypasses the temporal
        // filter on its own, so a test built on it proves nothing about IncludeHistory. IncludeHistory is
        // a scope that lifts exactly one filter, so the assertion is about the method.
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadRepository<StaffAssignment>>();
        var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();

        using (StaffAssignment.IncludeHistory(filters))
        {
            (await repository.GetByIdAsync(active.PersistenceId)).Should().NotBeNull(
                "IncludeHistory should return the active record");
            (await repository.GetByIdAsync(expired.PersistenceId)).Should().NotBeNull(
                "IncludeHistory should return the expired record as well");
        }

        // The control: outside the scope the closed stretch is hidden
        // again, which is what makes the assertion above about IncludeHistory and not about the read.
        (await repository.GetByIdAsync(expired.PersistenceId)).Should().BeNull(
            "the temporal filter is back on, so the closed stretch is not visible");
        (await repository.GetByIdAsync(active.PersistenceId)).Should().NotBeNull(
            "and the open stretch still is");
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

        var now = DateTimeOffset.UtcNow;

        // The zero: one open stretch is fine, and this is the write path under test.
        await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyId, "First", now.AddDays(-10)));

        // A second one for the same property, left open, through the same repository — no mutation, so
        // no ValidateTemporalConstraints anywhere on this path.
        var write = async () => await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyId, "Second", now.AddDays(-1)));

        await write.Should().ThrowAsync<Exception>(
            "the partial unique index holds on every write path, not only through a mutation");

        // The control: a CLOSED second stretch is not a second open one, so the index admits it. Without
        // this the assertion above would also hold on an index that refused every second row.
        var closed = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Closed", now.AddDays(-90));
        closed.ValidTo = now.AddDays(-60);

        await WriteAsync(closed);
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
    ///         and does not care in which order the store emits its statements.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AStretchEndingBeforeItBegins_IsRefusedByTheDatabase()
    {
        var propertyId = await CreatePropertyIdAsync();

        var now = DateTimeOffset.UtcNow;

        var inverted = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Impossible", now.AddDays(-5));
        inverted.ValidTo = now.AddDays(-30);

        var write = async () => await WriteAsync(inverted);

        await write.Should().ThrowAsync<Exception>(
            "an interval cannot end before it starts, and the database is where that is enforced");

        // The control: the same row with its dates the right way round is accepted, so the refusal is
        // the invariant and not something else about the write.
        var ordered = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Ordered", now.AddDays(-30));
        ordered.ValidTo = now.AddDays(-5);

        await WriteAsync(ordered);
    }

    // =========================================================================
    // ValidateTemporalConstraintsAsync: MaxActive = 1 prevents second active
    // =========================================================================

    [Fact]
    public async Task ValidateTemporalConstraints_MaxActiveExceeded_ReturnsError()
    {
        var propertyId = await CreatePropertyIdAsync();

        var now = DateTimeOffset.UtcNow;

        // First active assignment
        await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyId, "FirstManager", now.AddDays(-10)));

        // Try to add a second active assignment for the SAME property
        var second = StaffAssignment.Create(Guid.NewGuid(), propertyId, "SecondManager", now);

        using var scope = Services.CreateScope();
        var error = await second.ValidateTemporalConstraintsAsync(
            scope.ServiceProvider.GetRequiredService<IReadRepository<StaffAssignment>>());

        error.Should().NotBeNull(
            "MaxActive=1 should be violated when adding a second active assignment for the same property");
        error!.ViolationType.Should().Be(Pragmatic.Persistence.Entity.TemporalViolationType.MaxActiveExceeded);
        error.MaxActive.Should().Be(1);
    }

    // =========================================================================
    // ValidateTemporalConstraintsAsync: different property → no conflict
    // =========================================================================

    [Fact]
    public async Task ValidateTemporalConstraints_DifferentProperty_NoConflict()
    {
        var propertyIdA = await CreatePropertyIdAsync();
        var propertyIdB = await CreatePropertyIdAsync();

        var now = DateTimeOffset.UtcNow;

        // Active assignment for property A
        await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyIdA, "Manager", now.AddDays(-10)));

        // New assignment for property B — should NOT conflict
        var assignmentB = StaffAssignment.Create(Guid.NewGuid(), propertyIdB, "Manager", now);

        using var scope = Services.CreateScope();
        var error = await assignmentB.ValidateTemporalConstraintsAsync(
            scope.ServiceProvider.GetRequiredService<IReadRepository<StaffAssignment>>());

        error.Should().BeNull(
            "MaxActive is scoped per Property — different properties should not conflict");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Writes the assignments through the boundary's repository, in one save of its unit of work.</summary>
    private async Task WriteAsync(params StaffAssignment[] assignments)
    {
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<StaffAssignment>>();
        foreach (var assignment in assignments)
            repository.Add(assignment);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BookingBoundary)).SaveChangesAsync();
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
