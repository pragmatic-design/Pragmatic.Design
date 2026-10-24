using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[ConcurrencyAware]</c> on the Sql engine (owner's decisions of 2026-10-22): the version is the database's own where it has one
///     (PostgreSQL's <c>xmin</c>, SQL Server's <c>rowversion</c>) and a BIGINT the writes advance elsewhere; a stale write is Pragmatic's
///     <see cref="ConcurrencyError" />, raised as <see cref="PersistenceRuleViolationException" />. Five databases.
/// </summary>
[Collection("pragmatic databases")]
public class ConcurrencyTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static SqlUnitOfWork UnitOfWork(SqlSession session) => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_second_of_two_writers_of_a_row_meets_a_concurrency_error(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var seeding = UnitOfWork(session);
        var wifi = new Amenity { Name = "WiFi" };
        new Amenity.SqlRepository(seeding).Add(wifi);
        await seeding.SaveChangesAsync();
        var inserted = wifi.RowVersion; // read back from the insert where the database keeps it

        // Two units of work read the same row at the same version.
        var (first, second) = (UnitOfWork(session), UnitOfWork(session));
        var mine = (await new Amenity.SqlRepository(first).GetByIdAsync(wifi.Id))!;
        var theirs = (await new Amenity.SqlRepository(second).GetByIdAsync(wifi.Id))!;
        Assert.Equal(inserted, theirs.RowVersion);

        mine.Name = "WiFi 6";
        await first.SaveChangesAsync();
        Assert.NotEqual(inserted, mine.RowVersion); // advanced, and known to the writer: it can write again
        mine.Name = "WiFi 7";
        await first.SaveChangesAsync();

        theirs.Name = "Fibre";
        var stale = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => second.SaveChangesAsync());
        var conflict = Assert.IsType<ConcurrencyError>(stale.Error);
        Assert.Equal(("CONCURRENCY_CONFLICT", 409, "Amenity"), (conflict.Code, conflict.StatusCode, conflict.EntityTypeName));
        Assert.Equal("WiFi 7", (await UnitOfWorkTests.RawAsync<Amenity>(session, _toggle)).Single(a => a.Id == wifi.Id).Name);

        // A delete at a stale version is refused too; at the current one it goes.
        var reader = UnitOfWork(session);
        var current = (await new Amenity.SqlRepository(reader).GetByIdAsync(wifi.Id))!;
        new Amenity.SqlRepository(second).Remove(theirs);
        await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => second.SaveChangesAsync());
        new Amenity.SqlRepository(reader).Remove(current);
        Assert.Equal(1, await reader.SaveChangesAsync());
        Assert.DoesNotContain(await UnitOfWorkTests.RawAsync<Amenity>(session, _toggle), a => a.Id == wifi.Id);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task ClosesEfGap_the_version_advances_on_every_database(SqlDialect dialect)
    {
        // On EF Core the token of a provider other than SQL Server and PostgreSQL is a uint nothing advances (BoundaryDbContextTemplate):
        // two writers never conflict there. Here every write moves it, and what the row holds is what the entity reads.
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await UnitOfWorkTests.SeededAsync(databases, dialect, _toggle);
        var unitOfWork = UnitOfWork(session);
        var spa = new Amenity { Name = "Spa" };
        new Amenity.SqlRepository(unitOfWork).Add(spa);
        await unitOfWork.SaveChangesAsync();
        var versions = new List<ulong> { spa.RowVersion };
        foreach (var name in new[] { "Spa+", "Spa++" })
        {
            spa.Name = name;
            await unitOfWork.SaveChangesAsync();
            versions.Add(spa.RowVersion);
            Assert.Equal(spa.RowVersion, (await UnitOfWorkTests.RawAsync<Amenity>(session, _toggle)).Single(a => a.Id == spa.Id).RowVersion);
        }

        Assert.Equal(3, versions.Distinct().Count());
    }
}
