using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Result.EntityFrameworkCore.Sqlite;
using Xunit;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

public class SaveChangesResultTests
{
    // Builds a DI container whose SqliteTestDbContext is created via AddDbContext, so the context
    // carries an ApplicationServiceProvider from which the registry can be resolved (finding #37).
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton(connection);
        services.AddDbContext<SqliteTestDbContext>(o => o.UseSqlite(connection));
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<SqliteTestDbContext>().Database.EnsureCreated();

        return provider;
    }

    private static async Task<T> UseContextAsync<T>(ServiceProvider provider, Func<SqliteTestDbContext, Task<T>> act)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteTestDbContext>();
        return await act(context).ConfigureAwait(true);
    }

    [Fact]
    public async Task SaveChangesAsResultAsync_WithRegisteredProviderParser_HonorsRegistry()
    {
        // A stub parser classifies every DbUpdateException as a foreign-key violation. If the common
        // SaveChangesAsResultAsync path consults the resolved registry (rather than its old inline
        // heuristics), a genuine UNIQUE violation is reported as the stub's ForeignKey/DbConstraintError.
        using var provider = BuildProvider(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbExceptionParser>(new StubForeignKeyParser()));
            services.TryAddSingleton(sp => new DbExceptionParserRegistry(sp.GetServices<IDbExceptionParser>()));
        });

        var result = await UseContextAsync(provider, async context =>
        {
            context.Entities.Add(new SqliteUniqueEntity { Email = "dup@example.com" });
            context.Entities.Add(new SqliteUniqueEntity { Email = "dup@example.com" });
            return await context.SaveChangesAsResultAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Match(
            () => "success",
            _ => "conflict",
            constraint => $"constraint:{constraint.ConstraintType}:{constraint.TableName}")
            .Should().Be("constraint:ForeignKey:StubTable");
    }

    [Fact]
    public async Task SaveChangesAsResultAsync_NoRegistryRegistered_FallsBackToHeuristic()
    {
        // No provider error handling registered → registry resolves to Default (heuristic only), which
        // still classifies the SQLite "UNIQUE constraint failed" message as a unique violation.
        using var provider = BuildProvider();

        var result = await UseContextAsync(provider, async context =>
        {
            context.Entities.Add(new SqliteUniqueEntity { Email = "same@example.com" });
            context.Entities.Add(new SqliteUniqueEntity { Email = "same@example.com" });
            return await context.SaveChangesAsResultAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Match(() => false, _ => true, _ => false).Should().BeTrue("unique violation maps to DbConflictError");
    }

    [Fact]
    public async Task SaveChangesAsResultAsync_WithSqliteProvider_ClassifiesUniqueAsConflict()
    {
        using var provider = BuildProvider(services => services.AddSqliteResultErrorHandling());

        var result = await UseContextAsync(provider, async context =>
        {
            context.Entities.Add(new SqliteUniqueEntity { Email = "a@example.com" });
            context.Entities.Add(new SqliteUniqueEntity { Email = "a@example.com" });
            return await context.SaveChangesAsResultAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        result.IsFailure.Should().BeTrue();
        result.Match(() => false, _ => true, _ => false).Should().BeTrue();
    }

    [Fact]
    public async Task SaveChangesAsResultAsync_Success_ReturnsSuccess()
    {
        using var provider = BuildProvider(services => services.AddSqliteResultErrorHandling());

        var result = await UseContextAsync(provider, async context =>
        {
            context.Entities.Add(new SqliteUniqueEntity { Email = "unique@example.com" });
            return await context.SaveChangesAsResultAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
    }

    private sealed class StubForeignKeyParser : IDbExceptionParser
    {
        public string ProviderName => "Stub";
        public bool CanParse(Exception exception) => true;

        public DbErrorInfo? Parse(Exception exception)
            => new DbErrorInfo
            {
                ErrorType = DbErrorType.ForeignKeyConstraint,
                TableName = "StubTable",
                ConstraintName = "STUB_FK"
            };
    }
}
