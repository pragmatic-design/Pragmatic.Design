// Pragmatic.Composition.HostWiring.Tests - A row version advances where the database keeps none

using System.Runtime.Loader;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     On a provider whose database does not advance a row's version (SQLite here; SQL Server's <c>rowversion</c> and PostgreSQL's
///     <c>xmin</c> do), the generated boundary context advances a <c>[ConcurrencyAware]</c> entity's <c>RowVersion</c> on every update,
///     so a copy read before another writer's save is refused.
/// </summary>
/// <remarks>
///     Measured on the generated context against a database, not on its text: a token nothing advances compiles, maps and is never
///     compared against anything but itself.
/// </remarks>
public sealed class ARowVersionAdvancesWhereTheDatabaseKeepsNoneTests
{
    private const string Entity = """
        using Pragmatic.Persistence.Entity;

        namespace TimeOff.Leave.Entities;

        [Entity]
        [ConcurrencyAware]
        public partial class Employee : IEntity
        {
            public string Name { get; set; } = "";
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using TimeOff.Leave;

        namespace TimeOff.Host
        {
            [PragmaticDatabase(Provider = DatabaseProvider.SQLite, ConfigKey = "ConnectionStrings:App")]
            public sealed class AppDatabase : PragmaticDatabase;

            [Module]
            [Include<LeaveModule, AppDatabase>]
            public sealed class TimeOffHostModule;

            internal static class Program
            {
                private static Task Main(string[] args) => Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public async Task AStaleCopy_IsRefused_AfterAnotherWritersSave()
    {
        var (errors, module, host) = ModuleAndHost.Emit(
            "TimeOff.Leave", [AModuleWithoutEntitiesOnADatabaseTests.ModuleWithoutEntities, Entity], "TimeOff.Host", Host,
            withoutReference: name => name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase));

        errors.Should().BeEmpty("the host has to compile, or there is no context to save through");

        var loadContext = new AssemblyLoadContext("row-version", isCollectible: true);
        try
        {
            var moduleAssembly = loadContext.LoadFromStream(new MemoryStream(module));
            loadContext.Resolving += (_, name) => name.Name == moduleAssembly.GetName().Name ? moduleAssembly : null;
            var hostAssembly = loadContext.LoadFromStream(new MemoryStream(host!));
            var contextType = hostAssembly.GetType("TimeOff.Leave.Entities.LeaveDbContext", throwOnError: true)!;
            var employeeType = moduleAssembly.GetType("TimeOff.Leave.Entities.Employee", throwOnError: true)!;

            using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            await using var writer = NewContext(contextType, connection);
            await writer.Database.EnsureCreatedAsync();
            var employee = Activator.CreateInstance(employeeType)!;
            Rename(employee, "Ada");
            writer.Add(employee);
            await writer.SaveChangesAsync();

            await using var other = NewContext(contextType, connection);
            var stale = (await other.FindAsync(employeeType, Key(employee)))!;

            Rename(employee, "Grace");
            await writer.SaveChangesAsync();
            RowVersion(employee).Should().Be(1UL, "the update advanced the version from the one the insert wrote");

            Rename(stale, "Edsger");
            var refused = async () => await other.SaveChangesAsync().ConfigureAwait(false);
            await refused.Should().ThrowAsync<DbUpdateConcurrencyException>("the stale copy names the version the other writer moved past");
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static DbContext NewContext(Type contextType, SqliteConnection connection)
    {
        var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        options.UseSqlite(connection);
        return (DbContext)Activator.CreateInstance(contextType, options.Options)!;
    }

    private static void Rename(object employee, string name)
        => employee.GetType().GetProperty("Name")!.SetValue(employee, name);

    private static object Key(object employee) => employee.GetType().GetProperty("PersistenceId")!.GetValue(employee)!;

    private static ulong RowVersion(object employee) => (ulong)employee.GetType().GetProperty("RowVersion")!.GetValue(employee)!;
}
