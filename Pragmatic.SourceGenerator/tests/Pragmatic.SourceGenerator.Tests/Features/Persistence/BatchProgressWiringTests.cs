using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A boundary marked <c>[EnableBatchProgress]</c> maps the <c>__BatchProgress</c> table into its
///     generated DbContext (<c>OnModelCreating</c>) and into the schema metadata (so migrations create it),
///     mirroring <c>[EnableSagaPersistence]</c>. When the flag is off, none of it is emitted. The
///     fail-closed tenant query filter is emitted only when the host is also multi-tenant.
/// </summary>
public class BatchProgressWiringTests
{
    private static BoundaryDbContextModel BuildModel(bool hasBatchProgress, bool hasTenantEntity = false) => new()
    {
        Namespace = "MyApp.Sales.Entities",
        ClassName = "SalesDbContext",
        BoundaryName = "Sales",
        BoundaryTypeName = "MyApp.Sales.SalesBoundary",
        EfCoreProvider = EfCoreProvider.PostgreSql,
        HasBatchProgress = hasBatchProgress,
        Entities = new[]
        {
            new DbContextEntityModel
            {
                FullTypeName = "MyApp.Sales.Entities.Order",
                TypeName = "Order",
                DbSetName = "Orders",
                ConfigurationTypeName = "OrderEntityConfig",
                IsTenantEntity = hasTenantEntity,
            },
        }.ToEquatableArray(),
    };

    [Fact]
    public void OnModelCreating_WithBatchProgress_MapsBatchProgressTable()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasBatchProgress: true)).RenderOutput().Text;

        source.Should().Contain("new global::Pragmatic.Messaging.Batch.BatchProgressEntityTypeConfiguration()");
    }

    [Fact]
    public void OnModelCreating_WithoutBatchProgress_DoesNotMapBatchProgressTable()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasBatchProgress: false)).RenderOutput().Text;

        source.Should().NotContain("BatchProgressEntityTypeConfiguration");
    }

    [Fact]
    public void OnModelCreating_MultiTenant_EmitsBatchTenantQueryFilter()
    {
        var source = new BoundaryDbContextTemplate(
            BuildModel(hasBatchProgress: true, hasTenantEntity: true)).RenderOutput().Text;

        source.Should().Contain("global::Pragmatic.Messaging.Batch.BatchProgress>().HasQueryFilter(\"Tenant\"");
    }

    [Fact]
    public void OnModelCreating_SingleTenant_DoesNotEmitBatchTenantQueryFilter()
    {
        var source = new BoundaryDbContextTemplate(
            BuildModel(hasBatchProgress: true, hasTenantEntity: false)).RenderOutput().Text;

        source.Should().NotContain("global::Pragmatic.Messaging.Batch.BatchProgress>().HasQueryFilter");
    }

    [Fact]
    public void Schema_WithBatchProgress_IncludesBatchProgressTable()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasBatchProgress: true);

        var tables = model.Tables.AsImmutableArray();
        tables.Should().Contain(t => t.TableName == "__BatchProgress");
        var batch = tables.First(t => t.TableName == "__BatchProgress");
        // TenantId is NOT NULL (part of the tenant-isolation contract).
        batch.Columns.AsImmutableArray().Should().Contain(c => c.Name == "TenantId" && !c.IsNullable);
    }

    [Fact]
    public void Schema_WithoutBatchProgress_OmitsBatchProgressTable()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasBatchProgress: false);

        model.Tables.AsImmutableArray().Should().NotContain(t => t.TableName == "__BatchProgress");
    }

    private static ImmutableArray<EntityMetadataModel> SampleEntities() =>
        ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            Accessibility = "public",
            IsValid = true,
            IsFromReference = true,
            BoundaryName = "Sales",
            BoundaryTypeFullName = "MyApp.Sales.SalesBoundary",
        });
}
