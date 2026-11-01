// Pragmatic.Composition.HostWiring.Tests - The engine boundary of a module that also has an EF Core one reaches the host

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A module with a boundary on EF Core and one on the Sql engine: the host reads the module's entities from its Persistence metadata, so
///     that metadata carries the engine boundary's entities too. It carried EF Core's alone, and everything the host builds from the entities
///     of its modules missed the others: the audit trail's places (<see cref="TheAuditTrailOfAnEngineIsRegisteredWhereItIsWrittenTests" />),
///     and the permission catalogue a host's own operation reads the entity's read permission from.
/// </summary>
public sealed class TheEngineBoundaryOfAModuleWithAnEfCoreOneReachesTheHostTests
{
    private const string Module = """
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
            [Entity] [BelongsTo<Shop.Ledger.LedgerBoundary>]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }

            [Entity] [BelongsTo<Shop.Ledger.PayrollBoundary>]
            public partial class Payslip : IEntity
            {
                public string Employee { get; private set; } = "";
            }
        }

        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    /// <summary>
    ///     A host's own operation loading the Sql boundary's entity with its read permission: the permission is found in the catalogue the
    ///     host builds from its modules' entities. With the entity missing from the module's metadata, the host reported PRAG0457, "none is
    ///     known", and the load could not be generated.
    /// </summary>
    [Fact]
    public void AHostOperationLoadingTheEngineBoundarysEntity_FindsItsReadPermission()
    {
        var (errors, diagnostics) = ModuleAndHost.GenerateChain([("Shop.Ledger", [Module])], "Shop.Host", """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Composition.Database;
            using Pragmatic.Composition.Enums;
            using Pragmatic.Result;
            using Shop.Ledger;
            using Shop.Ledger.Entities;

            namespace Shop.Host;

            [PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
            public sealed class AppDatabase : PragmaticDatabase;

            [Module]
            [Include<LedgerModule, AppDatabase>]
            public sealed class ShopHostModule;

            [DomainAction]
            [LoadEntity<Payslip>(nameof(PayslipId), RequireReadPermission = true)]
            public partial class ReadPayslipAction : DomainAction<bool>
            {
                public required Guid PayslipId { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }

            internal static class Program
            {
                private static Task Main(string[] args) => Task.CompletedTask;
            }
            """);

        diagnostics.Select(d => d.Id).Should().NotContain("PRAG0457", "the Sql boundary's entity reaches the host, and its read permission with it");
        errors.Where(e => e.Contains("PRAG", StringComparison.Ordinal)).Should().BeEmpty();
    }
}
