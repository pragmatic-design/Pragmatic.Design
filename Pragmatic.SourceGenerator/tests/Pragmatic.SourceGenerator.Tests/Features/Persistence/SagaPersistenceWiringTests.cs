using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A boundary marked <c>[EnableSagaPersistence]</c> maps the saga tables into its generated
///     DbContext (<c>OnModelCreating</c>) and into the schema metadata (so migrations create them),
///     mirroring the <c>[EnableEventOutbox]</c> feature. When the flag is off, none of it is emitted.
///     Detection is metadata-based (boundaries live in referenced assemblies) and a boundary without
///     a <c>Pragmatic.Messaging.EFCore</c> reference reports PRAG0832 instead of silently doing nothing.
/// </summary>
public class SagaPersistenceWiringTests
{
    private static BoundaryDbContextModel BuildModel(bool hasSagaPersistence) => new()
    {
        Namespace = "MyApp.Sales.Entities",
        ClassName = "SalesDbContext",
        BoundaryName = "Sales",
        BoundaryTypeName = "MyApp.Sales.SalesBoundary",
        EfCoreProvider = EfCoreProvider.PostgreSql,
        HasSagaPersistence = hasSagaPersistence,
        Entities = new[]
        {
            new DbContextEntityModel
            {
                FullTypeName = "MyApp.Sales.Entities.Order",
                TypeName = "Order",
                DbSetName = "Orders",
                ConfigurationTypeName = "OrderEntityConfig",
            },
        }.ToEquatableArray(),
    };

    // --- Site 1: OnModelCreating maps the saga tables ------------------------------------------

    [Fact]
    public void OnModelCreating_WithSagaPersistence_MapsSagaTables()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasSagaPersistence: true)).RenderOutput().Text;

        source.Should().Contain("new global::Pragmatic.Messaging.Saga.SagaEntityTypeConfiguration()");
        source.Should().Contain("ApplyConfiguration<global::Pragmatic.Messaging.Saga.SagaInstance>");
        source.Should().Contain("ApplyConfiguration<global::Pragmatic.Messaging.Saga.SagaStep>");
    }

    [Fact]
    public void OnModelCreating_WithoutSagaPersistence_DoesNotMapSagaTables()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasSagaPersistence: false)).RenderOutput().Text;

        source.Should().NotContain("SagaEntityTypeConfiguration");
    }

    // --- Site 2: schema metadata includes both saga tables -------------------------------------

    [Fact]
    public void Schema_WithSagaPersistence_IncludesBothSagaTables()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasSagaPersistence: true);

        var tables = model.Tables.AsImmutableArray();
        tables.Should().Contain(t => t.TableName == "__SagaInstances");
        tables.Should().Contain(t => t.TableName == "__SagaSteps");
    }

    [Fact]
    public void Schema_WithoutSagaPersistence_OmitsSagaTables()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasSagaPersistence: false);

        var tables = model.Tables.AsImmutableArray();
        tables.Should().NotContain(t => t.TableName == "__SagaInstances");
        tables.Should().NotContain(t => t.TableName == "__SagaSteps");
    }

    [Fact]
    public void Schema_SagaStepsTable_HasCascadeForeignKeyToInstances()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasSagaPersistence: true);

        var steps = model.Tables.AsImmutableArray().Single(t => t.TableName == "__SagaSteps");
        var fk = steps.ForeignKeys.AsImmutableArray().Should().ContainSingle().Subject;
        fk.ReferencedTable.Should().Be("__SagaInstances");
        fk.Column.Should().Be("SagaInstanceId");
        fk.OnDelete.Should().Be("Cascade");
    }

    private static ImmutableArray<EntityMetadataModel> SampleEntities() => ImmutableArray.Create(
        new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            IsValid = true,
        });

    // --- Detection: [EnableSagaPersistence] on a boundary marker is read from metadata -----------

    [Fact]
    public void Reader_BoundaryWithAttribute_IsDetected()
    {
        ReadBoundaries(hasAttribute: true).AsImmutableArray()
            .Should().ContainSingle().Which.Should().Be("MyApp.Sales.SalesBoundary");
    }

    [Fact]
    public void Reader_BoundaryWithoutAttribute_IsNotDetected()
    {
        ReadBoundaries(hasAttribute: false).AsImmutableArray().Should().BeEmpty();
    }

    private static EquatableArray<string> ReadBoundaries(bool hasAttribute)
    {
        // Attribute stub inline so no Pragmatic.Messaging.Core reference is needed; the reader resolves
        // the boundary type via GetTypeByMetadataName and inspects its attributes.
        var attr = hasAttribute ? "[Pragmatic.Messaging.Attributes.EnableSagaPersistence]" : "";
        var source = $$"""
            namespace Pragmatic.Messaging.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class EnableSagaPersistenceAttribute : System.Attribute { }
            }
            namespace MyApp.Sales
            {
                {{attr}}
                public class SalesBoundary { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var entities = ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            IsValid = true,
            BoundaryTypeFullName = "MyApp.Sales.SalesBoundary",
        });

        return SagaPersistenceBoundaryReader.ReadEnabledBoundaries(result.OutputCompilation, entities, CancellationToken.None);
    }

    // --- Diagnostic: [EnableSagaPersistence] without Pragmatic.Messaging.EFCore is not a no-op ---

    [Fact]
    public void Diagnostic_EnableSagaPersistenceWithoutEFCore_ReportsPrag0832()
    {
        var source = """
            namespace Pragmatic.Messaging.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class EnableSagaPersistenceAttribute : System.Attribute { }
            }
            namespace MyApp.Sales
            {
                [Pragmatic.Messaging.Attributes.EnableSagaPersistence]
                public class SalesBoundary { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0832").Should().BeTrue();
    }
}
