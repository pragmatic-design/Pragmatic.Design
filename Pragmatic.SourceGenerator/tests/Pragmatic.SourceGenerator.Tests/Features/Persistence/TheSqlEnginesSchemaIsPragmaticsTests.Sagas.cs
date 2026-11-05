using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[EnableSagaPersistence]</c> (#529): the Sql engine's <c>__SagaInstances</c> and <c>__SagaSteps</c>, Pragmatic.Messaging.Engines' rows,
///     are EF Core's tables, column for column, index for index and key for key; on MySQL and Oracle, which have no partial index, the active
///     sagas' unique index is left out and reported (PRAG3038).
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>A boundary keeping its sagas, with one entity: a database's schema exists only where an entity lives.</summary>
    private const string SagaBoundary = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Sagas
        {
            [Pragmatic.Actions.Attributes.Boundary]
            [Pragmatic.Messaging.Attributes.EnableSagaPersistence]
            public sealed partial class SagasBoundary;

            [Entity]
            public partial class Desk : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }
        """;

    /// <summary>Pragmatic.Messaging.EFCore, by the type the generator recognises it by.</summary>
    private const string MessagingEfCore = "namespace Pragmatic.Messaging.EFCore { public sealed class EfCoreOutboxSource { } }";

    private static readonly Type[] SagaAttributes =
        [typeof(global::Pragmatic.Actions.Attributes.BoundaryAttribute), typeof(global::Pragmatic.Messaging.Attributes.EnableSagaPersistenceAttribute)];

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void TheSagaTables_AreTheSameTablesOnBothBranches(string provider)
    {
        var (efOutput, _) = Run([SagaBoundary, EfCoreGate, Host, ProviderProbe(provider), MessagingEfCore], SagaAttributes);
        var efCore = Describe(Read(Generated(efOutput, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList();
        var (engine, diagnostics) = SagaSchema(DriverProbe(provider));
        diagnostics.Should().NotContain(d => d.Id == "PRAG3027", "the rows have a schema on the Sql engine");
        diagnostics.Should().NotContain(d => d.Id == "PRAG3038", "the dialect has partial indexes");

        efCore.Should().Contain(l => l.Trim() == "index IX___SagaInstances_SagaType_CorrelationId_TenantId (SagaType, CorrelationId, TenantId) UNIQUE WHERE \"Status\" = 0",
            "the control: EF Core's tables are there, with the active sagas' unique index");
        efCore.Should().Contain(l => l.Trim().StartsWith("fk FK___SagaSteps___SagaInstances_SagaInstanceId", StringComparison.Ordinal), "the control: and the steps' key");
        Sagas(efCore).Except(Sagas(engine)).Concat(Sagas(engine).Except(Sagas(efCore))).Should().BeEmpty();
    }

    /// <summary>
    ///     On MySQL and Oracle the schema has the tables and every index but the partial one, and says so on the boundary; the control is
    ///     PostgreSQL, where the same boundary has the index and nothing is reported.
    /// </summary>
    [Theory]
    [InlineData("namespace MySqlConnector { public sealed class MySqlConnection { } }", "MySQL")]
    [InlineData("namespace Oracle.ManagedDataAccess.Client { public sealed class OracleConnection { } }", "Oracle")]
    public void OnADatabaseWithoutPartialIndexes_TheActiveSagasIndexIsLeftOut_AndReported(string driver, string database)
    {
        var (schema, diagnostics) = SagaSchema(driver);

        schema.Should().Contain(l => l.Trim().StartsWith("index IX___SagaInstances_Status_TimeoutAt", StringComparison.Ordinal), "the other indexes are there");
        schema.Should().NotContain(l => l.Contains("IX___SagaInstances_SagaType_CorrelationId_TenantId", StringComparison.Ordinal));
        var reported = diagnostics.Where(d => d.Id == "PRAG3038").ToList();
        reported.Should().ContainSingle();
        reported[0].GetMessage().Should().Contain("SagasBoundary").And.Contain(database);

        var (control, quiet) = SagaSchema(DriverProbe("PostgreSql"));
        control.Should().Contain(l => l.Contains("IX___SagaInstances_SagaType_CorrelationId_TenantId", StringComparison.Ordinal));
        quiet.Should().NotContain(d => d.Id == "PRAG3038");
    }

    /// <summary>The Sql engine's schema of <see cref="SagaBoundary" /> on the driver <paramref name="driver" /> stands for, with Pragmatic.Messaging.Engines' rows.</summary>
    private static (List<string> Schema, System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics) SagaSchema(string driver)
    {
        var (output, diagnostics) = Run([SagaBoundary, Host, SqlBridge, driver, PackageSource("ISagaInstanceRecord.cs"), PackageSource("ISagaStepRecord.cs"),
            PackageSource("SagaInstanceRow.cs"), PackageSource("SagaStepRow.cs")], [typeof(global::Pragmatic.Persistence.Sql.SqlSession), .. SagaAttributes]);
        return (Describe(Read(Generated(output, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList(), diagnostics);
    }

    /// <summary>The lines of the two saga tables only: the boundary's own entity is compared by the other tests.</summary>
    private static List<string> Sagas(IEnumerable<string> schema)
        => [.. schema.Where(l => l.Contains("__Saga", StringComparison.Ordinal))];
}
