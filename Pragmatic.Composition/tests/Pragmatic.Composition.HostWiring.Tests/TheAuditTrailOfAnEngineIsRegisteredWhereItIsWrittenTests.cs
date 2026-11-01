// Pragmatic.Composition.HostWiring.Tests - The audit trail of an engine's boundary is registered where its entries are written

using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host whose <c>[Audited]</c> entities live in one boundary of the Sql or documents engine registers the trail's store on that
///     boundary's session, after the <c>configure</c> callback, as it registers EF Core's on the one database that holds them. Two places, and
///     the host registers none and says so: the trail is read from one, and which is the application's to name.
/// </summary>
/// <remarks>
///     The Sql engine's bridge is stood for by its marker type, as in <see cref="ADatabaseOfTheSqlEngineIsMigratedAtStartTests" />; the
///     documents engine's is referenced for its test only (<see cref="HostWiringFixture.EngineBridge" />). Pragmatic.Audit.Engines is the real
///     one: the registration is compiled against the methods it calls.
/// </remarks>
public sealed class TheAuditTrailOfAnEngineIsRegisteredWhereItIsWrittenTests
{
    private const string SqlMarker = """

        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private const string OneSqlBoundary = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Ledger
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class LedgerBoundary;

            [Module(Name = "Shop.Ledger")]
            public sealed class LedgerModule;
        }

        namespace Shop.Ledger.Entities
        {
            [Entity] [Audited]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }
        }
        """ + SqlMarker;

    private const string TwoSqlBoundaries = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Ledger
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class LedgerBoundary;

            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class PayrollBoundary;

            [Module(Name = "Shop.Ledger")]
            public sealed class LedgerModule;
        }

        namespace Shop.Ledger.Entities
        {
            [Entity] [Audited] [BelongsTo<Shop.Ledger.LedgerBoundary>]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }

            [Entity] [Audited] [BelongsTo<Shop.Ledger.PayrollBoundary>]
            public partial class Payslip : IEntity
            {
                public string Employee { get; private set; } = "";
            }
        }
        """ + SqlMarker;

    private const string OneDocumentsBoundary = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Ledger
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Documents)]
            public partial class LedgerBoundary;

            [Module(Name = "Shop.Ledger")]
            public sealed class LedgerModule;
        }

        namespace Shop.Ledger.Entities
        {
            [Entity] [Audited]
            public partial class Letter : IEntity
            {
                public string Subject { get; private set; } = "";
            }
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Shop.Ledger;

        namespace Shop.Host;

        [Module]
        [Include<LedgerModule>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private static readonly MetadataReference AuditEngines = HostWiringFixture.EngineBridge("Pragmatic.Audit.Engines");

    [Fact]
    public void OneSqlBoundaryWithAuditedEntities_GetsTheTrail_AfterTheApplicationsConfiguration()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Ledger", [OneSqlBoundary], "Shop.Host", Host, [AuditEngines]);

        errors.Should().BeEmpty("the host has to compile, or the trail is registered by text nobody runs");
        RegisterAuditTrail(generated).Should().Contain(
            "global::Pragmatic.Audit.Engines.Sql.SqlAuditExtensions.AddSqlAuditTrail<global::Shop.Ledger.LedgerBoundary>(services);",
            "the boundary's saves write the entries, so its session is where the trail is read");

        var entry = generated.Values.Single(v => v.Contains("configure?.Invoke(pragmaticBuilder);", StringComparison.Ordinal)
                                                 && v.Contains("PragmaticHost.RegisterAuditTrail(builder.Services);", StringComparison.Ordinal));
        entry.IndexOf("PragmaticHost.RegisterAuditTrail(builder.Services);", StringComparison.Ordinal).Should().BeGreaterThan(
            entry.IndexOf("configure?.Invoke(pragmaticBuilder);", StringComparison.Ordinal),
            "after the application's configuration: a trail it registered there is the one it keeps (TryAdd)");
    }

    [Fact]
    public void OneDocumentsBoundaryWithAuditedEntities_GetsTheTrailOnItsSession()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Ledger", [OneDocumentsBoundary], "Shop.Host", Host,
            [AuditEngines, HostWiringFixture.EngineBridge("Pragmatic.Persistence.Documents")]);

        errors.Should().BeEmpty();
        RegisterAuditTrail(generated).Should().Contain(
            "global::Pragmatic.Audit.Engines.Documents.DocAuditExtensions.AddDocAuditTrail<global::Shop.Ledger.LedgerBoundary>(services);");
    }

    [Fact]
    public void TwoBoundariesWithAuditedEntities_RegisterNoTrail_AndSaySo()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Ledger", [TwoSqlBoundaries], "Shop.Host", Host, [AuditEngines]);

        errors.Should().BeEmpty();
        var method = RegisterAuditTrail(generated);
        method.Should().NotContain(">(services);", "the trail is read from one place, and which of the two is the application's to name");
        method.Should().Contain("[Audited] entities live in more than one place");
    }

    /// <summary>
    ///     An EF Core database and an engine's boundary, both with <c>[Audited]</c> entities, are two places: EF Core's rule, which registered
    ///     <c>AuditDbContext</c> when one database held them, steps aside with the engine's.
    /// </summary>
    /// <remarks>Two modules: <see cref="AnEfCoreAndASqlBoundaryOfOneModule_AreTwoPlaces_AndNeitherRegistersTheTrail" /> is the same in one.</remarks>
    [Fact]
    public void AnEfCoreDatabaseAndAnEnginesBoundary_AreTwoPlaces_AndNeitherRegistersTheTrail()
    {
        var (errors, generated) = ModuleAndHost.GenerateChainedSources(
            [("Shop.Ledger", [EfCoreLedger]), ("Shop.Payroll", [SqlPayroll])], "Shop.Host", HostOnADatabase, [AuditEngines]);

        errors.Should().BeEmpty();
        RegisterAuditTrail(generated).Should().NotContain(">(services);");
        generated.Values.Should().NotContain(v => v.Contains("AddDbContext<global::Pragmatic.Audit.EFCore.AuditDbContext>", StringComparison.Ordinal),
            "the EF Core database is one of two places, and the trail is read from one");
    }

    /// <summary>
    ///     One module with a boundary on EF Core and one on the Sql engine, each holding an <c>[Audited]</c> entity: two places, as with two
    ///     modules. The module's Persistence metadata carried its EF Core entities only, and the host reads a module's entities from that
    ///     metadata when there is one: the Sql boundary's entity never reached it, one place was counted, and EF Core's <c>AuditDbContext</c>
    ///     was registered as though its database were the only place the entries are written.
    /// </summary>
    [Fact]
    public void AnEfCoreAndASqlBoundaryOfOneModule_AreTwoPlaces_AndNeitherRegistersTheTrail()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Ledger", [EfCoreAndSqlLedger], "Shop.Host", HostOfOneModuleOnADatabase, [AuditEngines]);

        errors.Should().BeEmpty();
        RegisterAuditTrail(generated).Should().NotContain(">(services);");
        generated.Values.Should().NotContain(v => v.Contains("AddDbContext<global::Pragmatic.Audit.EFCore.AuditDbContext>", StringComparison.Ordinal),
            "the module's EF Core database is one of two places, and the trail is read from one");
    }

    private const string EfCoreAndSqlLedger = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Ledger
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.EfCore)]
            public partial class LedgerBoundary;

            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class PayrollBoundary;

            [Module(Name = "Shop.Ledger")]
            public sealed class LedgerModule;
        }

        namespace Shop.Ledger.Entities
        {
            [Entity] [Audited] [BelongsTo<Shop.Ledger.LedgerBoundary>]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }

            [Entity] [Audited] [BelongsTo<Shop.Ledger.PayrollBoundary>]
            public partial class Payslip : IEntity
            {
                public string Employee { get; private set; } = "";
            }
        }
        """ + SqlMarker;

    private const string HostOfOneModuleOnADatabase = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using Shop.Ledger;

        namespace Shop.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
        public sealed class AppDatabase : PragmaticDatabase;

        [Module]
        [Include<LedgerModule, AppDatabase>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private const string EfCoreLedger = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Ledger
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.EfCore)]
            public partial class LedgerBoundary;

            [Module(Name = "Shop.Ledger")]
            public sealed class LedgerModule;
        }

        namespace Shop.Ledger.Entities
        {
            [Entity] [Audited]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }
        }
        """;

    private const string SqlPayroll = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop.Payroll
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class PayrollBoundary;

            [Module(Name = "Shop.Payroll")]
            public sealed class PayrollModule;
        }

        namespace Shop.Payroll.Entities
        {
            [Entity] [Audited]
            public partial class Payslip : IEntity
            {
                public string Employee { get; private set; } = "";
            }
        }
        """ + SqlMarker;

    private const string HostOnADatabase = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Database;
        using Pragmatic.Composition.Enums;
        using Shop.Ledger;
        using Shop.Payroll;

        namespace Shop.Host;

        [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
        public sealed class AppDatabase : PragmaticDatabase;

        [Module]
        [Include<LedgerModule, AppDatabase>]
        [Include<PayrollModule>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void ABoundaryWithoutAuditedEntities_HasNoTrailToRegister()
    {
        var (errors, generated) = ModuleAndHost.Generate("Shop.Ledger", [OneSqlBoundary.Replace("[Entity] [Audited]", "[Entity]")], "Shop.Host", Host,
            [AuditEngines]);

        errors.Should().BeEmpty();
        generated.Values.Should().NotContain(v => v.Contains("RegisterAuditTrail", StringComparison.Ordinal),
            "nothing writes entries, so there is no trail to read");
    }

    private static string RegisterAuditTrail(IReadOnlyDictionary<string, string> generated)
    {
        var host = generated.Values.SingleOrDefault(v => v.Contains("RegisterAuditTrail(this IServiceCollection services)", StringComparison.Ordinal));
        host.Should().NotBeNull("an engine's boundary holds [Audited] entities, and the host decides where the trail is read");
        var start = host!.IndexOf("RegisterAuditTrail(this IServiceCollection services)", StringComparison.Ordinal);
        return host.Substring(start, host.IndexOf("return services;", start, StringComparison.Ordinal) - start);
    }
}
