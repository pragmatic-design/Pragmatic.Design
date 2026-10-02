using Conformance.Catalog.Entities;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     What <c>[ReadAccess&lt;TEntity&gt;]</c> grants.
/// </summary>
/// <remarks>
///     <para>
///         <c>SalesBoundary</c> declares <c>[ReadAccess&lt;CatalogItem&gt;]</c>. The generator adds to
///         <c>SalesDbContext</c> a <c>DbSet&lt;CatalogItem&gt;</c> and a single line of configuration,
///         <c>ExcludeFromMigrations()</c>, because the owner creates the table.
///     </para>
///     <para>
///         The name says «read». These cases measure whether the mechanism enforces it instead of
///         inferring it from the name.
///     </para>
///     <para>
///         ⚠️ The write goes through EF's API (<c>Entry(...).Property(...).CurrentValue</c>) and not through
///         a generated setter, which is <c>internal</c> to the module's assembly. It is the most precise
///         shape for the question asked: it shows that the <b>context</b> is what allows or refuses the
///         write, however closed the entity is.
///     </para>
/// </remarks>
public class ReadAccessAcrossTheBoundary(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>The owner creates the row: it owns the table.</summary>
    /// <remarks>
    ///     ⚠️ The queries filter by <c>PersistenceId</c>, not by <c>Id</c>: the latter is a computed
    ///     expression and not a column. See <c>TheDomainIdentity</c>.
    /// </remarks>
    private async Task<Guid> AnItemOwnedByCatalogAsync(decimal price)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var item = CatalogItem.Create(price);
        catalog.Set<CatalogItem>().Add(item);
        await catalog.SaveChangesAsync();

        return item.PersistenceId;
    }

    /// <summary>
    ///     What the attribute exists for: reading from the other side of the boundary, with a SQL JOIN on
    ///     the same physical database.
    /// </summary>
    [Fact]
    public async Task ReadAccess_LetsTheOtherBoundaryRead()
    {
        var id = await AnItemOwnedByCatalogAsync(12.50m);

        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var seen = await sales.Set<CatalogItem>().AsNoTracking().SingleAsync(i => i.PersistenceId == id);

        seen.ListPrice.Should().Be(12.50m,
            "it is the owner's table, read through the DbSet that [ReadAccess] adds");
    }

    /// <summary>
    ///     ⚠️ The name tells the truth: writing across the boundary is refused.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>DbSet&lt;T&gt;</c> is writable, and the only configuration added is
    ///         <c>ExcludeFromMigrations()</c> — which is about the schema, not permissions. Without a
    ///         further check the owner's row would really change across the boundary, with nothing to stop
    ///         it. The boundary's <c>DbContext</c> refuses, in <c>SaveChanges</c>, every change to an entity
    ///         it holds only for reading.
    ///     </para>
    ///     <para>
    ///         The check sits on the <b>save</b> and not on the <c>DbSet</c>: the commit is what crosses the
    ///         boundary. Reading those rows, keeping them tracked, even changing them in memory stays legal —
    ///         what is not is committing them from here.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task WritingAcrossTheBoundary_IsRefused()
    {
        var id = await AnItemOwnedByCatalogAsync(9.00m);

        await using (var scope = Services.CreateAsyncScope())
        {
            var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

            var item = await sales.Set<CatalogItem>().SingleAsync(i => i.PersistenceId == id);
            sales.Entry(item).Property(nameof(CatalogItem.ListPrice)).CurrentValue = 99.00m;

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sales.SaveChangesAsync());

            refused.Message.Should().Contain("ReadAccess",
                "the message must say which mechanism granted the read and not the write");
        }

        await using var check = Services.CreateAsyncScope();
        var catalog = check.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var reread = await catalog.Set<CatalogItem>().AsNoTracking().SingleAsync(i => i.PersistenceId == id);
        reread.ListPrice.Should().Be(9.00m,
            "and the owner's row did not change: the refusal comes before the database");
    }

    /// <summary>
    ///     The control case: the owner writes its own rows as always.
    /// </summary>
    /// <remarks>
    ///     Without it, a <c>SaveChanges</c> that refuses everything would pass the case above. The
    ///     guarantee is «not your rows», not «no rows».
    /// </remarks>
    [Fact]
    public async Task TheOwnerStillWritesItsOwnRows()
    {
        var id = await AnItemOwnedByCatalogAsync(9.00m);

        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var item = await catalog.Set<CatalogItem>().SingleAsync(i => i.PersistenceId == id);
        catalog.Entry(item).Property(nameof(CatalogItem.ListPrice)).CurrentValue = 42.00m;

        var written = await catalog.SaveChangesAsync();

        written.Should().Be(1, "the owning boundary is not constrained by [ReadAccess]");
    }
}
