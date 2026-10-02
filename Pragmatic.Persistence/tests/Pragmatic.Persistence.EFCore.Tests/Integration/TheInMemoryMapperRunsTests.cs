using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A query that maps in memory is executed, not merely generated.
/// </summary>
/// <remarks>
///     <para>
///         The generator tests assert what the emitted <c>Apply</c> file says. That is not the same as
///         the executor doing it: <c>MapEach</c> could be rendered perfectly and read by nobody, which
///         is the failure this repository keeps finding. These cases run the executor.
///     </para>
///     <para>
///         ⚠️ The mapper here does something no SQL projection could — it formats a decimal into a
///         string with a currency word. If the executor were quietly projecting, or casting, the
///         assertion could not pass.
///     </para>
/// </remarks>
public class TheInMemoryMapperRunsTests : IDisposable
{
    private readonly TestDbContext _db;

    public TheInMemoryMapperRunsTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"MapInMemory_{Guid.NewGuid():N}")
            .Options;

        _db = new TestDbContext(options);
        _db.Products.AddRange(
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Anvil", Price = 12.5m },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Rope", Price = 3m },
            new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Antidote", Price = 99m });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The claim: the rows come back mapped by the Func, filter and all.</summary>
    [Fact]
    public async Task AQueryWithAMapper_AnswersMappedRows()
    {
        var executor = new EfCoreQueryExecutor(null, null, null, null);

        var rows = await executor.ExecuteAllAsync(new ProductRowQuery(), _db.Products);

        rows.Should().HaveCount(2, "the filter runs on the database, before anything is mapped");
        rows.Select(r => r.Label).Should().Contain("Anvil — 12.50 credits");
    }

    /// <summary>
    ///     The control: the filter really did run server-side, on the queryable.
    /// </summary>
    /// <remarks>
    ///     Without it, "two rows came back" is satisfied by materialising the whole table and filtering
    ///     in memory — which is the performance trap this option must not become. The third product is
    ///     excluded by the query's own <c>Apply</c>, so its absence is the filter's doing.
    /// </remarks>
    [Fact]
    public async Task TheFilter_NarrowsBeforeTheMapping()
    {
        var executor = new EfCoreQueryExecutor(null, null, null, null);
        var query = new ProductRowQuery();

        var applied = query.Apply(_db.Products);

        applied.Should().HaveCount(2, "Apply is what the executor hands to the database");
        (await executor.ExecuteAllAsync(query, _db.Products)).Should().HaveCount(2);
    }

    /// <summary>A query that maps in memory, hand-written in the shape the generator emits.</summary>
    private sealed class ProductRowQuery : IQuery<TestProduct, ProductRow>
    {
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query)
            => query.Where(p => p.Name.StartsWith("An"));

        // No Projection: this shape cannot be translated, which is the whole point.
        public Func<TestProduct, ProductRow>? MapEach =>
            // Invariant on purpose: a decimal formatted with the ambient culture would make this test
            // pass on the machine that wrote it and fail on the one that runs it.
            p => new ProductRow
            {
                Label = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{p.Name} — {p.Price:0.00} credits")
            };
    }

    private sealed class ProductRow
    {
        public string Label { get; init; } = "";
    }
}
