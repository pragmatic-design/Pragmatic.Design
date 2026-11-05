using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A Pragmatic entity hierarchy on the Sql engine (#536): <see cref="Charge" />, with <see cref="LateCharge" /> and <see cref="DamageCharge" />
///     in one table, every member behind a private setter, on the base and on the derived types. Written through each type's generated
///     repository, read back through the base's as their derived types, with every member. Five databases.
/// </summary>
[Collection("pragmatic databases")]
public class EntityHierarchyTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_derived_types_are_written_and_read_back_through_the_base_with_every_member(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());

        var late = LateCharge.Create(hoursLate: 0);
        late.Charged(40m, "late check-out");
        late.Late(3);
        var damage = DamageCharge.Create();
        damage.Charged(120m, "broken lamp");
        damage.Damaged("lamp");
        new LateCharge.SqlRepository(unitOfWork).Add(late);
        new DamageCharge.SqlRepository(unitOfWork).Add(damage);
        await unitOfWork.SaveChangesAsync();

        IReadRepository<Charge> charges = new Charge.SqlReadRepository(session);
        var readLate = Assert.IsType<LateCharge>(await charges.GetByIdAsync(late.Id));
        Assert.Equal((40m, "late check-out", 3), (readLate.Amount, readLate.Reason, readLate.HoursLate));
        var readDamage = Assert.IsType<DamageCharge>(await charges.GetByIdAsync(damage.Id));
        Assert.Equal((120m, "broken lamp", "lamp"), (readDamage.Amount, readDamage.Reason, readDamage.Item));
        Assert.Equal(UnitOfWorkTests.Now, readLate.CreatedAt); // the root's audit stamps, written through the derived type

        // A derived type's own reads see its rows only; the base's, both.
        Assert.Equal([late.Id], (await session.Query<LateCharge>().ToListAsync()).Select(c => c.Id));
        Assert.Equal(2, (await session.Query<Charge>().ToListAsync()).Count);
    }
}
