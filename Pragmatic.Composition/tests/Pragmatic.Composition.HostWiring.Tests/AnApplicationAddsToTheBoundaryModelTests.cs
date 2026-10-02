// Pragmatic.Composition.HostWiring.Tests - The application's part of a generated boundary DbContext

using System.Runtime.Loader;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The generated boundary <c>DbContext</c> calls <c>partial void OnModelCreatingPartial(ModelBuilder)</c>
///     after every generated configuration, so the application's part of the class can add to the model.
/// </summary>
/// <remarks>
///     <para>
///         Without this seam there is no way in: <c>OnModelCreating</c> is a generated override, so a
///         partial class cannot add to it, and what is left is EF Core's own
///         <c>ReplaceService&lt;IModelCustomizer, …&gt;</c>, untested with the generated registration.
///     </para>
///     <para>
///         Measured on the model built from the context, not on the generated text: a hook that is
///         declared and never called reads exactly like one that works.
///     </para>
/// </remarks>
public sealed class AnApplicationAddsToTheBoundaryModelTests
{
    private const string ApplicationIndex = "IX_Employees_Name_ByTheApplication";

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using TimeOff.Leave;

        namespace TimeOff.Host
        {
            [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
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

    private const string ApplicationPart = """

        namespace TimeOff.Leave.Entities
        {
            using Microsoft.EntityFrameworkCore;

            public partial class LeaveDbContext
            {
                partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Employee>()
                        .HasIndex(e => e.Name)
                        .HasDatabaseName("IX_Employees_Name_ByTheApplication");
            }
        }
        """;

    [Fact]
    public void ThePartialHook_AddsToTheModelBuiltFromTheContext()
    {
        var indexes = IndexesOfEmployee(Host + ApplicationPart);

        indexes.Should().Contain(ApplicationIndex,
            "the application's part of the context runs after the generated configuration");
    }

    /// <summary>
    ///     The control: without a partial the context compiles and builds the model it built before, and
    ///     the partial adds exactly its index to it — nothing generated is dropped or replaced.
    /// </summary>
    [Fact]
    public void WithoutAPartial_TheModelIsTheGeneratedOne()
    {
        var generated = IndexesOfEmployee(Host);
        var extended = IndexesOfEmployee(Host + ApplicationPart);

        generated.Should().NotContain(ApplicationIndex);
        extended.Except(generated).Should().Equal(ApplicationIndex);
        generated.Except(extended).Should().BeEmpty("the hook adds to the model, it does not rebuild it");
    }

    /// <summary>The index names on <c>Employee</c> in the model <c>LeaveDbContext</c> builds.</summary>
    private static IReadOnlyList<string> IndexesOfEmployee(string hostSource)
    {
        var (errors, module, host) = ModuleAndHost.Emit(
            "TimeOff.Leave",
            [AModuleWithoutEntitiesOnADatabaseTests.ModuleWithoutEntities, AModuleWithoutEntitiesOnADatabaseTests.OneEntity],
            "TimeOff.Host",
            hostSource);

        errors.Should().BeEmpty("the host has to compile, or there is no context to build a model from");

        var context = new AssemblyLoadContext("model-customization", isCollectible: true);
        try
        {
            var moduleAssembly = context.LoadFromStream(new MemoryStream(module));
            context.Resolving += (_, name) => name.Name == moduleAssembly.GetName().Name ? moduleAssembly : null;
            var hostAssembly = context.LoadFromStream(new MemoryStream(host!));

            var contextType = hostAssembly.GetType("TimeOff.Leave.Entities.LeaveDbContext", throwOnError: true)!;
            var options = (DbContextOptionsBuilder)Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;

            // No connection is opened: building the model needs the provider, not the database.
            options.UseNpgsql("Host=unused");

            using var dbContext = (DbContext)Activator.CreateInstance(contextType, options.Options)!;
            var employee = dbContext.Model.FindEntityType("TimeOff.Leave.Entities.Employee");
            employee.Should().NotBeNull("the boundary context maps the module's entity");

            return [.. employee!.GetIndexes().Select(i => i.GetDatabaseName() ?? "").OrderBy(n => n, StringComparer.Ordinal)];
        }
        finally
        {
            context.Unload();
        }
    }
}
