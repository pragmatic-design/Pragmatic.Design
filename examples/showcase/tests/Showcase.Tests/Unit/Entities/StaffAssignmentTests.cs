using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Showcase.Booking.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests StaffAssignment temporal relation behavior:
/// ValidateTemporalConstraints, AutoClosePrevious, query extensions.
/// </summary>
public class StaffAssignmentTests
{
    /// <summary>A clock stopped at one instant, so "when" is an input instead of the wall clock.</summary>
    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    /// <summary>
    ///     The generated temporal filter — the one every read of this entity passes through — decides
    ///     "active" against the clock it was given.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ It read <c>DateTimeOffset.UtcNow</c> inline, so the global filter could only ever mean
    ///         "active as the wall clock sees it". Of the four sites this issue covers, it is the one
    ///         that cannot be worked around by a caller: an extension can be handed an instant, a global
    ///         filter cannot.
    ///     </para>
    ///     <para>
    ///         Executed, not read: the filter's expression is compiled and applied to rows in memory, so
    ///         what is asserted is the predicate's behaviour rather than the text that produced it.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheTemporalFilter_ReadsTheClockItWasBuiltWith()
    {
        var t0 = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var assignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", t0);
        assignment.ValidTo = t0.AddDays(7);

        var duringIt = new StaffAssignment.TemporalFilter(new FixedClock(t0.AddDays(1)))
            .GetFilter().Compile();
        var afterIt = new StaffAssignment.TemporalFilter(new FixedClock(t0.AddDays(30)))
            .GetFilter().Compile();
        var beforeIt = new StaffAssignment.TemporalFilter(new FixedClock(t0.AddDays(-1)))
            .GetFilter().Compile();

        duringIt(assignment).Should().BeTrue("the row is active a day into its window");
        afterIt(assignment).Should().BeFalse("ValidTo is a week out and the clock says a month passed");
        beforeIt(assignment).Should().BeFalse("and the day before ValidFrom it had not begun");
    }

    [Fact]
    public void Create_SetsValidFrom()
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now);

        assignment.ValidFrom.Should().Be(now);
        assignment.ValidTo.Should().BeNull();
    }

    [Fact]
    public void Create_ImplementsITemporalRelation()
    {
        var assignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", DateTimeOffset.UtcNow);

        assignment.Should().BeAssignableTo<ITemporalRelation>();
    }

    [Fact]
    public void ValidateTemporalConstraints_NoExisting_ReturnsNull()
    {
        var assignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", DateTimeOffset.UtcNow);
        var existing = Array.Empty<StaffAssignment>().AsQueryable();

        var error = assignment.ValidateTemporalConstraints(existing);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateTemporalConstraints_ActiveExistsSameProperty_ReturnsMaxActiveExceeded()
    {
        var now = DateTimeOffset.UtcNow;
        var propertyId = Guid.NewGuid();
        var newAssignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Manager", now);

        var existingAssignment = StaffAssignment.Create(Guid.NewGuid(), propertyId, "Manager", now.AddDays(-30));
        var existing = new[] { existingAssignment }.AsQueryable();

        var error = newAssignment.ValidateTemporalConstraints(existing);

        error.Should().NotBeNull();
        error!.ViolationType.Should().Be(TemporalViolationType.MaxActiveExceeded);
        error.MaxActive.Should().Be(1);
        error.CurrentActive.Should().Be(1);
    }

    [Fact]
    public void ValidateTemporalConstraints_ActiveExistsDifferentProperty_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var newAssignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now);

        // Existing active assignment for a DIFFERENT property — should NOT trigger MaxActive
        var existingAssignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now.AddDays(-30));
        var existing = new[] { existingAssignment }.AsQueryable();

        var error = newAssignment.ValidateTemporalConstraints(existing);

        error.Should().BeNull("MaxActive is scoped per Property, different properties don't conflict");
    }

    [Fact]
    public void ValidateTemporalConstraints_ExpiredExists_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var newAssignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now);

        var expiredAssignment = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now.AddDays(-60));
        expiredAssignment.ValidTo = now.AddDays(-1);
        var existing = new[] { expiredAssignment }.AsQueryable();

        var error = newAssignment.ValidateTemporalConstraints(existing);

        error.Should().BeNull();
    }

    [Fact]
    public void AutoClosePrevious_ClosesActiveRecords()
    {
        var now = DateTimeOffset.UtcNow;
        var active = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now.AddDays(-30));
        var existing = new[] { active }.AsQueryable();

        var parentId = active.PropertyId;
        StaffAssignment.AutoClosePrevious(existing, now, parentId);

        active.ValidTo.Should().Be(now);
    }

    [Fact]
    public void AutoClosePrevious_SkipsAlreadyClosed()
    {
        var now = DateTimeOffset.UtcNow;
        var closedBefore = StaffAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), "Manager", now.AddDays(-60));
        closedBefore.ValidTo = now.AddDays(-10);

        var existing = new[] { closedBefore }.AsQueryable();

        StaffAssignment.AutoClosePrevious(existing, now, closedBefore.PropertyId);

        // ValidTo unchanged — was already closed before closedAt
        closedBefore.ValidTo.Should().Be(now.AddDays(-10));
    }
}
