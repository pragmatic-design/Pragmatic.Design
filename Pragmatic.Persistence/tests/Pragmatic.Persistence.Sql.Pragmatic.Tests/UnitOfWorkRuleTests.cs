using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Pragmatic.Result.Database;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The rules the schema enforces, met by a unit of work's save, as <c>EfCoreUnitOfWork</c> reports them (<c>RuleViolationClassifier</c>): a
///     <see cref="PersistenceRuleViolationException" /> carrying a duplicate (409, the field named), a delete in use (409, what uses the row), or
///     another broken rule (400). The schema's names are Pragmatic.Migrations': <c>IX_Amenities_Name</c>, <c>FK_Reservations_PropertyId_Properties</c>.
/// </summary>
[Collection("pragmatic databases")]
public class UnitOfWorkRuleTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static SqlUnitOfWork UnitOfWork(SqlSession session) => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());

    private static async Task<T> BrokenAsync<T>(SqlUnitOfWork unitOfWork) where T : class
    {
        var broken = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => unitOfWork.SaveChangesAsync());        return Assert.IsType<T>(broken.Error);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_duplicate_is_a_conflict_naming_its_field(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var first = UnitOfWork(session);
        new Amenity.SqlRepository(first).Add(new Amenity { Name = "WiFi" });
        await first.SaveChangesAsync();

        var second = UnitOfWork(session);
        new Amenity.SqlRepository(second).Add(new Amenity { Name = "WiFi" });
        var duplicate = await BrokenAsync<DbConflictError>(second);
        Assert.Equal(("DB_CONFLICT", 409, "Amenity", "Name", "Duplicate value for Name."), (duplicate.Code, duplicate.StatusCode, duplicate.EntityType, duplicate.FieldName, duplicate.Reason));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_row_naming_a_missing_one_and_a_null_are_constraint_errors(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var orphan = UnitOfWork(session);
        var stay = new Reservation { CheckIn = UnitOfWorkTests.Now };
        stay.SetPropertyId(Guid.CreateVersion7()); // no such property
        new Reservation.SqlRepository(orphan).Add(stay);
        var missing = await BrokenAsync<DbConstraintError>(orphan);
        Assert.Equal(("DB_CONSTRAINT", 400, "ForeignKey"), (missing.Code, missing.StatusCode, missing.ConstraintType));

        var empty = UnitOfWork(session);
        new Amenity.SqlRepository(empty).Add(new Amenity { Name = null! }); // a non-null column left null
        var nullRule = await BrokenAsync<DbConstraintError>(empty);
        Assert.Equal(("DB_CONSTRAINT", 400, (string?)null), (nullRule.Code, nullRule.StatusCode, nullRule.ConstraintType));
        Assert.Empty(await UnitOfWorkTests.RawAsync<Amenity>(session, _toggle));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_delete_a_relation_refuses_is_in_use(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = UnitOfWork(session);
        IRepository<Property> properties = new Property.SqlRepository(unitOfWork);
        var riva = (await properties.FindAsync(Pragmatic.Specification.Spec<Property>.True)).Single(); // reservations and room types hang off it
        properties.Remove(riva);

        var inUse = await BrokenAsync<DbInUseError>(unitOfWork);
        Assert.Equal(("ENTITY_IN_USE", 409, "Property"), (inUse.Code, inUse.StatusCode, inUse.EntityType));
        // The relation is named by the database's constraint; SQLite names none, and two relations point at Property: not guessed.
        if (dialect == SqlDialect.Sqlite)
            Assert.Null(inUse.UsedBy);
        else
            Assert.Contains(inUse.UsedBy, new[] { "Reservation", "RoomType" });
        Assert.Contains(await UnitOfWorkTests.RawAsync<Property>(session, _toggle), p => p.Id == riva.Id);
    }
}
