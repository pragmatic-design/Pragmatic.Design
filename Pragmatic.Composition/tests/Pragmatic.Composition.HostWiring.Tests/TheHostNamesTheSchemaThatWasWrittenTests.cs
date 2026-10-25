// Pragmatic.Composition.HostWiring.Tests - The host migrates to the schema type the persistence generator wrote

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The host's start and its migration coordinator name the database's schema by the type the persistence generator wrote, whatever
///     namespace the boundary's entities are declared in.
/// </summary>
/// <remarks>
///     The schema's namespace is derived from the entities' (without a trailing <c>.Entities</c>, then without the last segment), and the
///     host derived it again from the module's type. The two agree only when the entities are in <c>{Module}.Entities</c>: with them in
///     <c>Shop.Orders.Catalog</c> the schema was <c>Shop.Orders.OrdersDatabaseSchema</c> and the host named <c>Shop.OrdersDatabaseSchema</c>,
///     which does not exist, so the host did not compile. On either engine.
///     The Sql engine's bridge is stood for by its marker type, in the Sql case only: with it, a boundary that names no engine would not be
///     EF Core's alone.
/// </remarks>
public sealed class TheHostNamesTheSchemaThatWasWrittenTests
{
    private static string Module(string engine) => $$"""
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Orders
        {
            [Boundary] {{engine}}
            public partial class OrdersBoundary;

            [Module(Name = "Shop.Orders")]
            public sealed class OrdersModule;
        }

        namespace Shop.Orders.Catalog
        {
            [Entity]
            public partial class Product : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }

        {{(engine.Length > 0 ? "namespace Pragmatic.Persistence.Sql { public static class PragmaticSqlFilters { } }" : "")}}
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using Shop.Orders;

        namespace Shop.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Orders")]
        public sealed class OrdersDatabase : PragmaticDatabase;

        [Module]
        [Include<OrdersModule, OrdersDatabase>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    [Theory]
    [InlineData("[PersistenceEngine(PersistenceEngineKind.Sql)]")]
    [InlineData("")]
    public void EntitiesOutsideTheirModulesEntitiesNamespace_AreMigratedToTheSchemaWritten(string engine)
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Orders", [Module(engine)], "Shop.Host", Host);

        errors.Should().BeEmpty("the host names the schema the persistence generator wrote, or it does not compile");
        generated["_Migration.Coordinator.g.cs"].Should().Contain("global::Shop.Orders.OrdersDatabaseSchema.Current");
    }
}
