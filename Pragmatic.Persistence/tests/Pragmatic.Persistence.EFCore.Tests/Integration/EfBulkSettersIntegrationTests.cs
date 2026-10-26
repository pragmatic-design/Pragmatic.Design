using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Bulk;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     The neutral setters of <see cref="IBulkRepository{TEntity}" /> over EF Core, on SQLite: the generated repository's <c>BulkUpdateAsync</c>
///     runs <c>ExecuteUpdateAsync(s =&gt; set(new EfBulkSetters&lt;T&gt;(s)))</c>, and both forms of <c>SetProperty</c> reach the rows, a
///     captured value and a value computed on each row. The generated text is <c>RepositoryTemplateTests.Repository_GeneratesBulkUpdateAsync</c>'s.
/// </summary>
public class EfBulkSettersIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BulkTestDbContext _db;

    public EfBulkSettersIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new BulkTestDbContext(new DbContextOptionsBuilder<BulkTestDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task The_setters_write_a_captured_value_and_a_value_computed_on_each_row()
    {
        _db.Products.AddRange(
            new BulkProduct { PersistenceId = Guid.NewGuid(), Name = "cheap", Price = 1m },
            new BulkProduct { PersistenceId = Guid.NewGuid(), Name = "dear", Price = 9m });
        await _db.SaveChangesAsync();

        var price = 5m;
        Action<IBulkSetters<BulkProduct>> set = s => s.SetProperty(p => p.Price, price).SetProperty(p => p.Name, p => p.Name + "!");
        var affected = await _db.Products.Where(p => p.Name == "cheap").ExecuteUpdateAsync(s => set(new EfBulkSetters<BulkProduct>(s)));

        affected.Should().Be(1);
        _db.ChangeTracker.Clear();
        var rows = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        rows.Select(p => (p.Name, p.Price)).Should().Equal(("cheap!", 5m), ("dear", 9m));
    }
}
