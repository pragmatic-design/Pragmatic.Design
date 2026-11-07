using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[TemporalRelation&lt;TParent&gt;(MaxActive = 1)]</c> (#523): one open stretch per parent is EF Core's partial unique index on the parent
///     key, <c>WHERE "ValidTo" IS NULL</c>, and the Sql engine's schema writes it too (the bridge suite's <c>KeyHolder</c> is compared line for
///     line by <see cref="TheSqlEnginesSchema_IsEfCoresBranch_ButForItsDepartures" />). On MySQL and Oracle, which have no partial index, it is
///     left out and reported (PRAG3039), as the active sagas' index is (PRAG3038).
/// </summary>
public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    private const string KeyHolderEntities = """
        global using System;
        using Pragmatic.Persistence.Entity;

        namespace Probe.Entities;

        [Entity]
        public partial class Property : IEntity
        {
            public string Name { get; private set; } = "";
        }

        [Entity]
        [Relation.ManyToOne<Property>]
        [TemporalRelation<Property>(MaxActive = 1)]
        public partial class KeyHolder : IEntity, ITemporalRelation
        {
            public string Person { get; set; } = "";
            public DateTimeOffset ValidFrom { get; set; }
            public DateTimeOffset? ValidTo { get; set; }
        }
        """;

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    public void OneOpenStretchPerParent_IsTheSamePartialUniqueIndexOnBothBranches(string provider)
    {
        var efCore = Describe(EfCoreSchema(provider, KeyHolderEntities)).ToList();
        var (sql, diagnostics) = SqlSchema(provider, KeyHolderEntities);
        diagnostics.Should().NotContain(d => d.Id == "PRAG3039", "the dialect has partial indexes");
        var engine = Describe(sql).ToList();

        efCore.Should().Contain(l => l.Trim() == "index IX_KeyHolders_PropertyId_Active (PropertyId) UNIQUE WHERE \"ValidTo\" IS NULL", "the control: EF Core's index is there");
        efCore.Except(engine).Concat(engine.Except(efCore)).Should().BeEmpty();
    }

    /// <summary>
    ///     On MySQL and Oracle the table keeps its other indexes and loses the partial one, and the entity says so; the control is PostgreSQL,
    ///     where the same entity has the index and nothing is reported.
    /// </summary>
    [Theory]
    [InlineData("namespace MySqlConnector { public sealed class MySqlConnection { } }", "MySQL")]
    [InlineData("namespace Oracle.ManagedDataAccess.Client { public sealed class OracleConnection { } }", "Oracle")]
    public void OnADatabaseWithoutPartialIndexes_TheOpenStretchIndexIsLeftOut_AndReported(string driver, string database)
    {
        var (schema, diagnostics) = KeyHolderSchema(driver);

        schema.Should().Contain(l => l.Trim().StartsWith("index IX_KeyHolders_PropertyId ", StringComparison.Ordinal), "the foreign key's index is there");
        schema.Should().NotContain(l => l.Contains("IX_KeyHolders_PropertyId_Active", StringComparison.Ordinal));
        var reported = diagnostics.Where(d => d.Id == "PRAG3039").ToList();
        reported.Should().ContainSingle();
        reported[0].GetMessage().Should().Contain("KeyHolder").And.Contain(database);

        var (control, quiet) = KeyHolderSchema(DriverProbe("PostgreSql"));
        control.Should().Contain(l => l.Contains("IX_KeyHolders_PropertyId_Active", StringComparison.Ordinal));
        quiet.Should().NotContain(d => d.Id == "PRAG3039");
    }

    /// <summary>The Sql engine's schema of <see cref="KeyHolderEntities" /> on the driver <paramref name="driver" /> stands for.</summary>
    private static (List<string> Schema, System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics) KeyHolderSchema(string driver)
    {
        var (output, diagnostics) = Run([KeyHolderEntities, Host, SqlBridge, driver], [typeof(global::Pragmatic.Persistence.Sql.SqlSession)]);
        return (Describe(Read(Generated(output, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current")).ToList(), diagnostics);
    }
}
