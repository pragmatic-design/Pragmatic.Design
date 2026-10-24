// Pragmatic.Composition.HostWiring.Tests - A database the Sql engine alone persists is migrated like any other

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that puts a module persisted by the Sql engine on a database migrates that database to its schema at start, and on demand,
///     as it does an EF Core one: the database has a schema (<c>{Db}Schema</c>), which is what the migration runner needs, and no EF Core
///     migration context, which only the EF Core fallback does.
/// </summary>
/// <remarks>
///     The Sql engine's bridge is stood for by its marker type, declared by the module: the bridge itself is not among this suite's
///     references, so every other test here keeps EF Core as the only engine its modules reference.
/// </remarks>
public sealed class ADatabaseOfTheSqlEngineIsMigratedAtStartTests
{
    private const string Module = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Reviews
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class ReviewsBoundary;

            [Module(Name = "Shop.Reviews")]
            public sealed class ReviewsModule;
        }

        namespace Shop.Reviews.Entities
        {
            [Entity]
            public partial class Review : IEntity
            {
                public string Text { get; private set; } = "";
            }
        }

        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using Shop.Reviews;

        namespace Shop.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Reviews")]
        public sealed class ReviewsDatabase : PragmaticDatabase;

        [Module]
        [Include<ReviewsModule, ReviewsDatabase>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void TheMigrationCoordinator_MigratesTheDatabaseToItsSchema()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host);

        errors.Should().BeEmpty("the host has to compile, or the migration is text nobody runs");
        generated.TryGetValue("_Migration.Coordinator.g.cs", out var coordinator).Should().BeTrue(
            "a database with a schema is migrated, whichever engine persists its boundaries");
        coordinator.Should().Contain("global::Shop.ReviewsDatabaseSchema.Current");
        coordinator.Should().Contain("configuration[\"ConnectionStrings:Reviews\"]");
    }

    [Fact]
    public void TheHostsStart_MigratesTheDatabaseWithTheRunner_AndHasNoEfCoreFallbackForIt()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Reviews", [Module], "Shop.Host", Host);

        errors.Should().BeEmpty();
        var start = generated.Where(g => g.Key != "_Migration.Coordinator.g.cs" && g.Value.Contains("migrationRunner.MigrateAsync", StringComparison.Ordinal))
            .Select(g => g.Value).SingleOrDefault();
        start.Should().NotBeNull("the host's start migrates its databases with Pragmatic.Migrations' runner");
        start.Should().Contain("global::Shop.ReviewsDatabaseSchema.Current");
        start.Should().NotContain("ReviewsDatabaseMigrationDbContext", "the EF Core fallback needs a migration context, which a database of the Sql engine has not");
    }
}
