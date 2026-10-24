using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Pragmatic.Result.Database;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>The mutation invoker's writes (<see cref="AmenityMutations" />) on the Sql engine's contracts, five databases.</summary>
[Collection("pragmatic databases")]
public class MutationSubstituteTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_create_and_an_update_go_through_the_contracts_and_a_failed_save_detaches(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
        IRepository<Amenity> amenities = new Amenity.SqlRepository(unitOfWork);

        var wifi = await AmenityMutations.CreateAsync(amenities, unitOfWork, "WiFi");
        var spa = await AmenityMutations.CreateAsync(amenities, unitOfWork, "Spa");
        var renamed = await AmenityMutations.RenameAsync(amenities, unitOfWork, wifi.Id, "WiFi 6");
        Assert.Equal("WiFi 6", renamed.Value.Name);

        var missing = await AmenityMutations.RenameAsync(amenities, unitOfWork, Guid.CreateVersion7(), "none");
        Assert.IsType<NotFoundError>(missing.Error);

        // A save that breaks a rule: the failure goes on, the entity is forgotten, the unit of work stays usable.
        var duplicate = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => AmenityMutations.RenameAsync(amenities, unitOfWork, spa.Id, "WiFi 6"));
        Assert.IsType<DbConflictError>(duplicate.Error);
        Assert.Null(unitOfWork.EntryOf(spa));
        Assert.Equal("Spa", (await UnitOfWorkTests.RawAsync<Amenity>(session, _toggle)).Single(a => a.Id == spa.Id).Name);
        Assert.Equal("Spa", (await AmenityMutations.RenameAsync(amenities, unitOfWork, spa.Id, "Spa")).Value.Name); // read again, as stored
    }
}
