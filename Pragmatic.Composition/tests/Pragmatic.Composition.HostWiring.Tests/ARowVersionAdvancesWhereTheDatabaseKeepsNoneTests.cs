// Pragmatic.Composition.HostWiring.Tests - A row version advances where the database keeps none

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
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
    public Task AStaleCopy_IsRefused_AfterAnotherWritersSave()
        => AfterAnotherWritersSave(async (other, _, _) =>
        {
            var refused = async () => await other.SaveChangesAsync().ConfigureAwait(false);
            await refused.Should().ThrowAsync<DbUpdateConcurrencyException>("the stale copy names the version the other writer moved past").ConfigureAwait(false);
        });

    /// <summary>
    ///     Through the generated repository the same stale save is its <c>ConcurrencyError</c>, read from the unit of work's rule violation:
    ///     a value, not an exception, as the repository's contract says.
    /// </summary>
    [Fact]
    public Task AStaleCopy_SavedThroughTheRepository_IsAConcurrencyError()
        => AfterAnotherWritersSave(async (other, employeeType, _) =>
        {
            var repositoryType = employeeType.GetNestedType("Repository")!;
            var repository = Activator.CreateInstance(
                repositoryType,
                BindingFlags.CreateInstance | BindingFlags.Public | BindingFlags.Instance | BindingFlags.OptionalParamBinding,
                binder: null,
                [other, new EfCoreUnitOfWork(other)],
                culture: null)!;
            var save = (Task<Result<int, ConcurrencyError>>)repositoryType.GetMethod("SaveChangesAsync", [typeof(CancellationToken)])!
                .Invoke(repository, [CancellationToken.None])!;

            var saved = await save.ConfigureAwait(false);

            saved.IsFailure.Should().BeTrue("the stale copy is refused");
            saved.Error.EntityTypeName.Should().Be(employeeType.FullName);
        });

    /// <summary>
    ///     A row two contexts read, saved by the first after a change: <paramref name="act" /> gets the second, still holding the version
    ///     the first moved past, with its copy changed.
    /// </summary>
    private static async Task AfterAnotherWritersSave(Func<DbContext, Type, object, Task> act)
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
            await connection.OpenAsync().ConfigureAwait(false);

            using var writer = NewContext(contextType, connection);
            await writer.Database.EnsureCreatedAsync().ConfigureAwait(false);
            var employee = Activator.CreateInstance(employeeType)!;
            Rename(employee, "Ada");
            writer.Add(employee);
            await writer.SaveChangesAsync().ConfigureAwait(false);

            using var other = NewContext(contextType, connection);
            var stale = (await other.FindAsync(employeeType, Key(employee)).ConfigureAwait(false))!;

            Rename(employee, "Grace");
            await writer.SaveChangesAsync().ConfigureAwait(false);
            RowVersion(employee).Should().Be(1UL, "the update advanced the version from the one the insert wrote");

            Rename(stale, "Edsger");
            await act(other, employeeType, stale).ConfigureAwait(false);
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
