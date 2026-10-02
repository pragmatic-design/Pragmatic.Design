using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

public class FindCancellationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SqliteTestDbContext> _options;

    public FindCancellationTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SqliteTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new SqliteTestDbContext(_options);
        context.Database.EnsureCreated();
        context.Entities.Add(new SqliteUniqueEntity { Id = 1, Email = "seed@example.com" });
        context.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task FindAsResultAsync_CompositeOverload_PropagatesCancellationToken()
    {
        // Fresh context: entity id 1 is not tracked, so FindAsync executes a query that honors the
        // token. A pre-cancelled token must surface as OperationCanceledException — proving the
        // composite overload passes the caller's token rather than CancellationToken.None.
        using var context = new SqliteTestDbContext(_options);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        var act = async () => await context.Entities
            .FindAsResultAsync("SqliteUniqueEntity", cts.Token, 1)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task FindAsResultAsync_CompositeOverload_NotCancelled_ReturnsEntity()
    {
        using var context = new SqliteTestDbContext(_options);

        var result = await context.Entities
            .FindAsResultAsync("SqliteUniqueEntity", CancellationToken.None, 1)
            .ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("seed@example.com");
    }
}
