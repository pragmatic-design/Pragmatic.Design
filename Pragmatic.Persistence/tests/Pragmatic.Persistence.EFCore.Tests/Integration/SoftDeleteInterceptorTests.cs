using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Identifiers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A delete of a soft-delete entity is the flag, whatever asked for it.
/// </summary>
/// <remarks>
///     <para>
///         The generated repository's <c>Remove</c> already wrote the flag, so every path through it
///         was right. The paths around it were not: a child leaving its parent's collection is severed
///         by EF and the row goes, and so does anything calling <c>context.Remove</c>. Measured on a
///         real aggregate before this existed — a curated collection lost a row that
///         <c>[SoftDelete]</c> said was recoverable.
///     </para>
///     <para>
///         Enforced at save time because that is where every path converges. What it must not do is
///         re-stamp: a cascade shares one instant and restore returns only the children carrying it.
///     </para>
/// </remarks>
public sealed class SoftDeleteInterceptorTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly TestDbContext _db;

    public SoftDeleteInterceptorTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"SoftDelete_{Guid.NewGuid():N}")
            .AddInterceptors(new SoftDeleteInterceptor(_clock))
            .Options;

        _db = new TestDbContext(options, _clock);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private async Task<TestOrder> AnOrderAsync()
    {
        var order = new TestOrder { PersistenceId = Guid7.New(), OrderNumber = "A-1", Total = 10m };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        return order;
    }

    [Fact]
    public async Task Removing_LeavesTheRowFlaggedRatherThanGone()
    {
        var order = await AnOrderAsync();

        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();

        var loaded = await _db.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.PersistenceId == order.PersistenceId);

        loaded.Should().NotBeNull("the row is what [SoftDelete] promises stays");
        loaded!.IsDeleted.Should().BeTrue();
        loaded.DeletedAt.Should().Be(_clock.GetUtcNow());
    }

    /// <remarks>
    ///     The half that keeps restore working. The repository stamps a cascade with one instant, and
    ///     restore brings back only what carries it — a second stamp written here would leave a child
    ///     that cannot come back with its parent.
    /// </remarks>
    [Fact]
    public async Task AnEntityAlreadyFlagged_KeepsItsOwnStamp()
    {
        var order = await AnOrderAsync();
        var stamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        order.IsDeleted = true;
        order.DeletedAt = stamp;
        order.DeletedBy = "the-cascade";
        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();

        var loaded = await _db.Orders.IgnoreQueryFilters()
            .FirstAsync(o => o.PersistenceId == order.PersistenceId);

        loaded.DeletedAt.Should().Be(stamp, "the cascade's instant is what restore matches on");
        loaded.DeletedBy.Should().Be("the-cascade");
    }

    /// <remarks>
    ///     Erasure has to be able to mean it. Without this, a subject's request under GDPR would flag
    ///     the row and report success, and the data would still be there.
    /// </remarks>
    [Fact]
    public async Task UnderASuspendedScope_TheRowIsReallyGone()
    {
        var order = await AnOrderAsync();

        using (SoftDeleteScope.Suspend())
        {
            _db.Orders.Remove(order);
            await _db.SaveChangesAsync();
        }

        var loaded = await _db.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.PersistenceId == order.PersistenceId);

        loaded.Should().BeNull("erasure said what it meant");
    }

    [Fact]
    public async Task TheScopeEnds_AndTheGuaranteeIsBack()
    {
        var erased = await AnOrderAsync();
        using (SoftDeleteScope.Suspend())
        {
            _db.Orders.Remove(erased);
            await _db.SaveChangesAsync();
        }

        var kept = await AnOrderAsync();
        _db.Orders.Remove(kept);
        await _db.SaveChangesAsync();

        var loaded = await _db.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.PersistenceId == kept.PersistenceId);

        loaded.Should().NotBeNull("the suspension covers its own scope and nothing after it");
        loaded!.IsDeleted.Should().BeTrue();
    }

    /// <remarks>
    ///     An entity that is not soft-delete is deleted, as it always was. Without this the test above
    ///     would pass on an interceptor that simply refused to delete anything.
    /// </remarks>
    [Fact]
    public async Task AnEntityThatIsNotSoftDelete_IsStillDeleted()
    {
        var customer = new TestCustomer { PersistenceId = Guid7.New(), FullName = "Ada", Email = "a@b.c" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();

        (await _db.Customers.FindAsync(customer.PersistenceId)).Should().BeNull();
    }
}
