using Conformance.Catalog.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A property's <c>[Column("…")]</c> names the column the generated schema creates, the one EF Core maps the property to (#568).
/// </summary>
/// <remarks>
///     The database is built from the schema Pragmatic generates (<c>ConformanceSchema</c>), not by EF Core: the two have to agree on the
///     column's name, or EF Core writes a column the table does not have.
/// </remarks>
public class TheNamedColumn(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task APropertyWithAColumnName_IsWrittenAndReadInThatColumn()
    {
        Guid id;
        await using (var scope = Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var item = CatalogItem.Create(3.10m);
            catalog.Set<CatalogItem>().Add(item);
            catalog.Entry(item).Property(nameof(CatalogItem.BuyerNote)).CurrentValue = "fragile";
            await catalog.SaveChangesAsync();
            id = item.PersistenceId;
        }

        await using var check = Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var reread = await db.Set<CatalogItem>().AsNoTracking().SingleAsync(i => i.PersistenceId == id);
        reread.BuyerNote.Should().Be("fragile", "EF Core reads back what it wrote");

        // By the column's name, around EF Core's mapping: the value is where the attribute says.
        var stored = await db.Database.SqlQuery<string>($"SELECT buyer_note AS \"Value\" FROM \"CatalogItems\" WHERE \"PersistenceId\" = {id}").SingleAsync();
        stored.Should().Be("fragile", "and the value is in the column the attribute names");
    }
}
