using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     <see cref="AGuardedCollectionIsFilteredWhateverReadsIt" /> on PostgreSQL: the filtered include,
///     <c>Count</c> over a <c>Where</c> and the projections are translated to SQL, not evaluated in memory.
/// </summary>
[Trait("Category", "Testcontainers")]
public sealed class AGuardedCollectionIsFilteredOnPostgresTests(PostgresContainerFixture fixture)
    : AGuardedCollectionIsFilteredWhateverReadsIt, IClassFixture<PostgresContainerFixture>
{
    protected override TestDbContext CreateContext()
    {
        var connection = new global::Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "guarded_" + Guid.NewGuid().ToString("N"),
        };

        return new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(connection.ConnectionString)
            .Options);
    }
}
