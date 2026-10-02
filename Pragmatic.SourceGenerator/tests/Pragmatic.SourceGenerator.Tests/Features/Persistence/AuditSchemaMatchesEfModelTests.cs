using System.Collections.Immutable;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Audit;
using Pragmatic.Audit.EFCore.Entities;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The migration schema and the EF model that reads it must describe the same <b>three</b> tables.
/// </summary>
/// <remarks>
///     <para>
///         Four places define this table: <c>AuditEntryEntityTypeConfiguration</c>, three SQL dialects in
///         <c>Pragmatic.Audit.AdoNet</c>, and <c>SchemaMetadataTransform.BuildAuditLogTable</c> — whose
///         own comment calls the duplication a hazard it "lives with". The dialects have their own test;
///         this one holds the schema transform to the EF model.
///     </para>
///     <para>
///         Without it, a column added to the entity and missed by the schema transform surfaces far from
///         the cause — a create returning 400 because the audit insert hits a column the migration never
///         created — and only with a database.
///     </para>
///     <para>
///         ⚠️ All three tables, not <c>__AuditEntries</c> alone: <c>AuditDbContext</c> has three
///         <c>DbSet</c>s, and a schema that emits two lets a host that turns the trail on write entries
///         and never seal a segment — "relation __AuditPrunedRanges does not exist".
///         <c>EveryTableTheContextReads_IsInTheSchema</c> is that assertion, and column agreement is
///         checked for each of the three.
///     </para>
/// </remarks>
public class AuditSchemaMatchesEfModelTests
{
    /// <summary>The audit tables the transform emits, by name.</summary>
    private static ImmutableArray<TableSchemaModel> AuditTables() =>
        [.. SchemaMetadataTransform
            .Transform(AuditedEntity(), EfCoreProvider.PostgreSql, "App", "MyApp")
            .Tables.AsImmutableArray()
            .Where(t => t.TableName.StartsWith("__Audit", StringComparison.Ordinal))];

    private static ImmutableArray<string> SchemaColumns(string tableName)
    {
        var table = AuditTables().First(t => t.TableName == tableName);

        return [.. table.Columns.AsImmutableArray().Select(c => c.Name).Order(StringComparer.Ordinal)];
    }

    /// <summary>The EF model, built from the configurations alone — no provider needed to read names.</summary>
    private static ImmutableArray<string> EfColumns(string tableName)
    {
        var builder = new ModelBuilder();
        builder.ApplyConfiguration(new AuditEntryEntityTypeConfiguration());
        builder.ApplyConfiguration(new AuditSegmentEntityTypeConfiguration());
        builder.ApplyConfiguration(new PrunedRangeEntityTypeConfiguration());

        var type = builder.Model.GetEntityTypes()
            .First(e => e.GetTableName() == tableName);

        return [.. type.GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal)];
    }

    [Theory]
    [InlineData(AuditEntryEntityTypeConfiguration.TableName)]
    [InlineData(AuditSegmentEntityTypeConfiguration.TableName)]
    [InlineData(PrunedRangeEntityTypeConfiguration.TableName)]
    public void TheSchemaAndTheEfModel_DescribeTheSameColumns(string tableName)
    {
        SchemaColumns(tableName).Should().Equal(EfColumns(tableName));
    }

    /// <summary>
    ///     ⚠️ The one that catches a whole table going missing, rather than a column.
    /// </summary>
    /// <remarks>
    ///     Column agreement per table says nothing about a table the transform never emits: the
    ///     comparison simply is not made. Reading the set of tables off the context is what makes the
    ///     absence visible, and it is how <c>__AuditPrunedRanges</c> was found — from a failing seal in
    ///     a consumer application rather than from here.
    /// </remarks>
    [Fact]
    public void EveryTableTheContextReads_IsInTheSchema()
    {
        string[] expected =
        [
            AuditEntryEntityTypeConfiguration.TableName,
            AuditSegmentEntityTypeConfiguration.TableName,
            PrunedRangeEntityTypeConfiguration.TableName
        ];

        AuditTables().Select(t => t.TableName).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void TheComparisonIsNotVacuous()
    {
        // Two empty sets are equal. If a table were not found, or the model came back without
        // properties, the assertions above would hold and prove nothing.
        SchemaColumns(AuditEntryEntityTypeConfiguration.TableName)
            .Should().Contain("Operation").And.Contain("SegmentId");
        EfColumns(AuditEntryEntityTypeConfiguration.TableName).Length.Should().BeGreaterThan(10);
        EfColumns(PrunedRangeEntityTypeConfiguration.TableName).Should().Contain("LinkHash");
    }

    private static ImmutableArray<EntityMetadataModel> AuditedEntity() =>
        ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            Accessibility = "public",
            IsValid = true,
            IsFromReference = true,
            IsAudited = true,
            BoundaryName = "Sales",
            BoundaryTypeFullName = "MyApp.Sales.SalesBoundary",
        });
}
