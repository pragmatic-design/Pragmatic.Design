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
///     A boundary marked <c>[EnableOutbox]</c> (the Messaging transport-publish outbox) maps the
///     <c>__OutboxMessages</c> table into its generated DbContext (<c>OnModelCreating</c>) and into the
///     schema metadata (so migrations create it), mirroring <c>[EnableSagaPersistence]</c> and
///     <c>[EnableEventOutbox]</c>. When the flag is off, none of it is emitted. Detection is
///     metadata-based; a boundary without <c>Pragmatic.Messaging.EFCore</c> reports PRAG0831, and a
///     boundary that also carries <c>[EnableEventOutbox]</c> reports PRAG0833 (both capture the same events).
/// </summary>
public class MessagingOutboxWiringTests
{
    private static BoundaryDbContextModel BuildModel(bool hasMessagingOutbox) => new()
    {
        Namespace = "MyApp.Sales.Entities",
        ClassName = "SalesDbContext",
        BoundaryName = "Sales",
        BoundaryTypeName = "MyApp.Sales.SalesBoundary",
        EfCoreProvider = EfCoreProvider.PostgreSql,
        HasMessagingOutbox = hasMessagingOutbox,
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

    // --- Site 1: OnModelCreating maps the outbox table -----------------------------------------

    [Fact]
    public void OnModelCreating_WithMessagingOutbox_MapsOutboxTable()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasMessagingOutbox: true)).RenderOutput().Text;

        source.Should().Contain("global::Pragmatic.Messaging.EFCore.Outbox.MessagingOutboxExtensions.AddMessagingOutbox(modelBuilder)");
    }

    [Fact]
    public void OnModelCreating_WithoutMessagingOutbox_DoesNotMapOutboxTable()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasMessagingOutbox: false)).RenderOutput().Text;

        source.Should().NotContain("MessagingOutboxExtensions");
    }

    // --- Site 2: schema metadata includes the outbox table -------------------------------------

    [Fact]
    public void Schema_WithMessagingOutbox_IncludesOutboxTable()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasMessagingOutbox: true);

        model.Tables.AsImmutableArray().Should().Contain(t => t.TableName == "__OutboxMessages");
    }

    [Fact]
    public void Schema_WithoutMessagingOutbox_OmitsOutboxTable()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasMessagingOutbox: false);

        model.Tables.AsImmutableArray().Should().NotContain(t => t.TableName == "__OutboxMessages");
    }

    [Fact]
    public void Schema_OutboxTable_HasKeyColumnsAndPollingIndex()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasMessagingOutbox: true);

        var outbox = model.Tables.AsImmutableArray().Single(t => t.TableName == "__OutboxMessages");
        var columns = outbox.Columns.AsImmutableArray();
        columns.Should().Contain(c => c.Name == "Id" && c.IsPrimaryKey);
        columns.Should().Contain(c => c.Name == "MessageType");
        columns.Should().Contain(c => c.Name == "Payload");
        columns.Should().Contain(c => c.Name == "ProcessedAt" && c.IsNullable);
        columns.Should().Contain(c => c.Name == "NextAttemptAt" && c.IsNullable);
        columns.Should().Contain(c => c.Name == "ClaimedBy" && c.IsNullable);

        // The polling index covers the claim query (unprocessed + backoff-eligible + unclaimed).
        outbox.Indexes.AsImmutableArray()
            .Should().Contain(i => i.Columns.AsImmutableArray().Contains("ProcessedAt")
                && i.Columns.AsImmutableArray().Contains("ClaimedUntil"));
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

    // --- Detection: [EnableOutbox] on a boundary marker is read from metadata ------------------

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
        var attr = hasAttribute ? "[Pragmatic.Messaging.Attributes.EnableOutbox]" : "";
        var source = $$"""
            namespace Pragmatic.Messaging.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class EnableOutboxAttribute : System.Attribute { }
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

        return MessagingOutboxBoundaryReader.ReadEnabledBoundaries(result.OutputCompilation, entities, CancellationToken.None);
    }

    // --- Diagnostics: PRAG0831 (no EFCore) and PRAG0833 (conflicting outboxes) ------------------

    [Fact]
    public void Diagnostic_EnableOutboxWithoutEFCore_ReportsPrag0831()
    {
        var source = """
            namespace Pragmatic.Messaging.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class EnableOutboxAttribute : System.Attribute { }
            }
            namespace MyApp.Sales
            {
                [Pragmatic.Messaging.Attributes.EnableOutbox]
                public class SalesBoundary { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0831").Should().BeTrue();
    }

    [Fact]
    public void Diagnostic_BothOutboxAttributes_ReportsPrag0833()
    {
        var source = """
            namespace Pragmatic.Messaging.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class EnableOutboxAttribute : System.Attribute { }
            }
            namespace Pragmatic.Events.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class EnableEventOutboxAttribute : System.Attribute { }
            }
            namespace MyApp.Sales
            {
                [Pragmatic.Messaging.Attributes.EnableOutbox]
                [Pragmatic.Events.Attributes.EnableEventOutbox]
                public class SalesBoundary { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0833").Should().BeTrue();
    }
}
