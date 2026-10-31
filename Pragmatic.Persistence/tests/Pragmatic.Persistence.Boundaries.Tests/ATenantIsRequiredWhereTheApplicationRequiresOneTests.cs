using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     A boundary on an engine refuses to write a tenant's entity with no tenant resolved when the application requires one
///     (<c>MultiTenancyOptions.RequireTenant</c>, on by default), as EF Core's generated registration does: the generated
///     <c>Add{Boundary}Persistence</c> reads the option and hands it to the unit of work.
/// </summary>
public sealed class ATenantIsRequiredWhereTheApplicationRequiresOneTests
{
    private const string Schema = """CREATE TABLE "Bookings" ("PersistenceId" TEXT PRIMARY KEY, "Guest" TEXT NOT NULL, "TenantId" TEXT NOT NULL);""";

    [Fact]
    public async Task With_a_tenant_required_and_none_resolved_a_tenants_entity_is_refused()
    {
        using var keeper = Database();
        await using var provider = Host(keeper.ConnectionString, requireTenant: true);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Booking>>().Add(new Booking { Guest = "Ada" });

        await Assert.ThrowsAsync<TenantNotResolvedException>(
            () => scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(FrontOfficeBoundary)).SaveChangesAsync());
    }

    /// <summary>Options nobody configured are the options' defaults, and the default requires a tenant.</summary>
    [Fact]
    public async Task With_the_options_never_configured_the_secure_default_refuses_the_write()
    {
        using var keeper = Database();
        await using var provider = Host(keeper.ConnectionString, requireTenant: null);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Booking>>().Add(new Booking { Guest = "Ada" });

        await Assert.ThrowsAsync<TenantNotResolvedException>(
            () => scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(FrontOfficeBoundary)).SaveChangesAsync());
    }

    /// <summary>The control: an application that runs without a tenant on purpose writes the row.</summary>
    [Fact]
    public async Task With_no_tenant_required_the_entity_is_written()
    {
        using var keeper = Database();
        await using var provider = Host(keeper.ConnectionString, requireTenant: false);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IRepository<Booking>>().Add(new Booking { Guest = "Ada" });

        Assert.Equal(1, await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(FrontOfficeBoundary)).SaveChangesAsync());
    }

    /// <summary>The front office's host; <paramref name="requireTenant" /> null leaves the options unconfigured (not even registered).</summary>
    private static ServiceProvider Host(string connectionString, bool? requireTenant)
    {
        var services = new ServiceCollection();
        if (requireTenant is { } required)
            services.Configure<MultiTenancyOptions>(o => o.RequireTenant = required);
        services.AddSingleton<ITenantContext>(new TenantScope()); // no tenant resolved
        services.AddFrontOfficeBoundaryPersistence((_, filters) =>
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return new SqlSession(connection, SqlDialect.Sqlite) { Filters = filters };
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>An in-memory SQLite database of the test's own with the bookings' table, kept alive by the returned connection.</summary>
    private static SqliteConnection Database()
    {
        var keeper = new SqliteConnection($"Data Source=a{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        keeper.Open();
        using var create = keeper.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
        return keeper;
    }
}
