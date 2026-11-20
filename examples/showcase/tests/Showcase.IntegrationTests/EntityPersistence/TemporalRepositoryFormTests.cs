using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     A temporal relation's point-in-time read and auto-close in their repository form (#899), on every engine: the specifications
///     <c>StaffAssignmentSpecifications.ActiveAt</c> and <c>ForProperty</c>, and <c>StaffAssignment.AutoClosePreviousAsync</c>.
/// </summary>
/// <remarks>
///     The sibling of <c>TemporalQueryExtensionsTests</c>, whose <c>IQueryable</c> extensions stay EF Core's: the same cases, written and read
///     through <c>IRepository&lt;StaffAssignment&gt;</c> and the boundary's unit of work, so the file runs on all three suites.
/// </remarks>
public class TemporalRepositoryFormTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheDefaultRead_ReturnsOnlyTheActiveStretch()
    {
        var propertyId = await CreatePropertyIdAsync();
        var now = DateTimeOffset.UtcNow;

        var expired = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Receptionist", now.AddDays(-60));
        expired.ValidTo = now.AddDays(-1);
        await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyId, "Manager", now.AddDays(-10)), expired);

        using var scope = Services.CreateScope();
        var active = await Repository(scope).FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId));

        active.Should().ContainSingle("the temporal filter leaves the expired stretch out of a read outside the history scope");
        active[0].Role.Should().Be("Manager");
    }

    [Fact]
    public async Task ActiveAt_InsideTheHistory_ReturnsWhoHeldItThen()
    {
        var propertyId = await CreatePropertyIdAsync();
        var now = DateTimeOffset.UtcNow;

        var past = StaffAssignment.Create(Guid.NewGuid(), propertyId, "OldManager", now.AddDays(-60));
        past.ValidTo = now.AddDays(-10);
        await WriteAsync(past, StaffAssignment.Create(Guid.NewGuid(), propertyId, "NewManager", now.AddDays(-5)));

        using var scope = Services.CreateScope();
        using var history = StaffAssignment.IncludeHistory(scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>());
        var repository = Repository(scope);

        var then = await repository.FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId) & StaffAssignmentSpecifications.ActiveAt(now.AddDays(-20)));
        then.Should().ContainSingle().Which.Role.Should().Be("OldManager", "it was the one active twenty days ago");

        var today = await repository.FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId) & StaffAssignmentSpecifications.ActiveAt(now));
        today.Should().ContainSingle().Which.Role.Should().Be("NewManager", "and the other one is active now");
    }

    /// <summary>
    ///     <c>ActiveAt</c> answers about the instant it is given, not the moment the test runs: both directions, since a read that returned
    ///     nothing whatever it was asked would satisfy only one.
    /// </summary>
    [Fact]
    public async Task ActiveAt_AnswersAboutTheInstantItIsGiven()
    {
        var propertyId = await CreatePropertyIdAsync();
        var now = DateTimeOffset.UtcNow;

        var assignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Manager", now.AddDays(-1));
        assignment.ValidTo = now.AddDays(7);
        await WriteAsync(assignment);

        using var scope = Services.CreateScope();
        using var history = StaffAssignment.IncludeHistory(scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>());
        var repository = Repository(scope);

        (await repository.FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId) & StaffAssignmentSpecifications.ActiveAt(now)))
            .Should().ContainSingle("the stretch is active now — the zero");
        (await repository.FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId) & StaffAssignmentSpecifications.ActiveAt(now.AddDays(30))))
            .Should().BeEmpty("ValidTo is a week out, and the instant a month on");
        (await repository.FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId) & StaffAssignmentSpecifications.ActiveAt(now.AddDays(-30))))
            .Should().BeEmpty("and ValidFrom is yesterday, so a month ago it had not begun");
    }

    [Fact]
    public async Task AutoClosePrevious_ClosesTheOpenStretch_AndTheNewOneIsTheOnlyActive()
    {
        var propertyId = await CreatePropertyIdAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyId, "OldManager", now.AddDays(-30)));

        var closedAt = now.AddSeconds(-1);
        using (var scope = Services.CreateScope())
        {
            var closed = await StaffAssignment.AutoClosePreviousAsync(
                Repository(scope), scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>(), closedAt, propertyId);

            closed.Should().ContainSingle("one stretch was open").Which.ValidTo.Should().Be(closedAt);
            await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BookingBoundary)).SaveChangesAsync();
        }

        await WriteAsync(StaffAssignment.Create(Guid.NewGuid(), propertyId, "NewManager", closedAt));

        using var read = Services.CreateScope();
        var active = await Repository(read).FindAsync(StaffAssignmentSpecifications.ForProperty(propertyId));
        active.Should().ContainSingle("the closed stretch ended a second ago").Which.Role.Should().Be("NewManager");
    }

    /// <summary>
    ///     The control for the history scope the auto-close opens: a stretch that starts tomorrow is not active now, so the default filter
    ///     hides it, and a close that read through that filter would leave it open beside the new one.
    /// </summary>
    [Fact]
    public async Task AutoClosePrevious_ClosesAStretchThatHasNotBegunYet()
    {
        var propertyId = await CreatePropertyIdAsync();
        var now = DateTimeOffset.UtcNow;
        var scheduled = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Scheduled", now.AddDays(1));
        await WriteAsync(scheduled);

        var closedAt = now.AddDays(5);
        using var scope = Services.CreateScope();
        var closed = await StaffAssignment.AutoClosePreviousAsync(
            Repository(scope), scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>(), closedAt, propertyId);

        closed.Should().ContainSingle("the open stretch is closed even though it is not active now")
            .Which.PersistenceId.Should().Be(scheduled.PersistenceId);
    }

    private static IRepository<StaffAssignment> Repository(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRepository<StaffAssignment>>();

    /// <summary>Writes the assignments through the boundary's repository, in one save of its unit of work.</summary>
    private async Task WriteAsync(params StaffAssignment[] assignments)
    {
        using var scope = Services.CreateScope();
        var repository = Repository(scope);
        foreach (var assignment in assignments)
            repository.Add(assignment);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BookingBoundary)).SaveChangesAsync();
    }

    /// <summary>A property of this test's own, so its assignments are alone in their partition.</summary>
    private async Task<Guid> CreatePropertyIdAsync()
    {
        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"TF-{Guid.NewGuid():N}"[..12],
            name = $"TempForm-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        return prop.GetProperty("id").GetGuid();
    }
}
