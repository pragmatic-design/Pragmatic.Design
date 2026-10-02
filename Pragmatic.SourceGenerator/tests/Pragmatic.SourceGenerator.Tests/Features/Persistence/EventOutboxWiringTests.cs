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
///     A boundary marked <c>[EnableEventOutbox]</c> wires the domain-event outbox into its
///     generated DbContext across all three codegen sites: the <c>__EventOutbox</c> table mapping in
///     <c>OnModelCreating</c>, the <c>EventOutboxInterceptor</c> + <c>AddEventOutbox&lt;T&gt;()</c> in the
///     DI registration, and the <c>__EventOutbox</c> table in the schema metadata (so migrations create it).
///     When the flag is off, none of it is emitted.
/// </summary>
public class EventOutboxWiringTests
{
    private static BoundaryDbContextModel BuildModel(bool hasEventOutbox) => new()
    {
        Namespace = "MyApp.Sales.Entities",
        ClassName = "SalesDbContext",
        BoundaryName = "Sales",
        BoundaryTypeName = "MyApp.Sales.SalesBoundary",
        EfCoreProvider = EfCoreProvider.PostgreSql,
        HasEventOutbox = hasEventOutbox,
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

    // --- Site 1: OnModelCreating maps __EventOutbox ---------------------------------------------

    [Fact]
    public void OnModelCreating_WithEventOutbox_MapsOutboxTable()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasEventOutbox: true)).RenderOutput().Text;

        source.Should().Contain(
            "modelBuilder.ApplyConfiguration(new global::Pragmatic.Events.EFCore.Outbox.EventOutboxEntryConfiguration());");
    }

    [Fact]
    public void OnModelCreating_WithoutEventOutbox_DoesNotMapOutboxTable()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasEventOutbox: false)).RenderOutput().Text;

        source.Should().NotContain("EventOutboxEntryConfiguration");
    }

    // --- Site 2: DI registration wires interceptor + delivery service --------------------------

    [Fact]
    public void Registration_WithEventOutbox_WiresInterceptorAndDeliveryService()
    {
        var template = new DbContextRegistrationTemplate(
            ImmutableArray.Create(BuildModel(hasEventOutbox: true)), "MyApp", hasMultiTenancy: false, hasEvents: true);

        var source = template.RenderOutput().Text;

        source.Should().Contain(
            "options.AddInterceptors(sp.GetRequiredService<global::Pragmatic.Events.EFCore.Outbox.EventOutboxInterceptor>());");
        source.Should().Contain(
            "global::Pragmatic.Events.EFCore.Outbox.EventOutboxExtensions.AddEventOutbox<SalesDbContext>(services);");
    }

    [Fact]
    public void Registration_WithoutEventOutbox_DoesNotWireOutbox()
    {
        var template = new DbContextRegistrationTemplate(
            ImmutableArray.Create(BuildModel(hasEventOutbox: false)), "MyApp", hasMultiTenancy: false, hasEvents: true);

        var source = template.RenderOutput().Text;

        source.Should().NotContain("EventOutboxInterceptor");
        source.Should().NotContain("AddEventOutbox");
    }

    // --- Site 3: schema metadata includes __EventOutbox ----------------------------------------

    [Fact]
    public void Schema_WithEventOutbox_IncludesOutboxTable()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasEventOutbox: true);

        model.Tables.AsImmutableArray().Should().Contain(t => t.TableName == "__EventOutbox");
    }

    [Fact]
    public void Schema_WithoutEventOutbox_OmitsOutboxTable()
    {
        var model = SchemaMetadataTransform.Transform(
            SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasEventOutbox: false);

        model.Tables.AsImmutableArray().Should().NotContain(t => t.TableName == "__EventOutbox");
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

    // --- Detection: [EnableEventOutbox] on a boundary marker is read from metadata ---------------

    [Fact]
    public void Reader_BoundaryWithAttribute_IsDetected()
    {
        var boundaries = ReadBoundaries("[EnableEventOutbox]");

        boundaries.AsImmutableArray().Should().ContainSingle().Which.Should().Be("MyApp.Sales.SalesBoundary");
    }

    [Fact]
    public void Reader_BoundaryWithoutAttribute_IsNotDetected()
    {
        var boundaries = ReadBoundaries(attributeLine: "");

        boundaries.AsImmutableArray().Should().BeEmpty();
    }

    private static EquatableArray<string> ReadBoundaries(string attributeLine)
    {
        var source = $$"""
            using Pragmatic.Events.Attributes;

            namespace MyApp.Sales;

            {{attributeLine}}
            public class SalesBoundary { }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<Pragmatic.Events.Attributes.EnableEventOutboxAttribute>());

        var entities = ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            IsValid = true,
            BoundaryTypeFullName = "MyApp.Sales.SalesBoundary",
        });

        return EventOutboxBoundaryReader.ReadEnabledBoundaries(result.OutputCompilation, entities, CancellationToken.None);
    }

    // --- Diagnostic: [EnableEventOutbox] without Pragmatic.Events.EFCore is not a silent no-op ---

    [Fact]
    public void Diagnostic_EnableEventOutboxWithoutEFCore_ReportsPrag2752()
    {
        // Only Pragmatic.Events (the attribute's package) is referenced — not Pragmatic.Events.EFCore —
        // so the outbox cannot be wired; the generator must say so rather than silently do nothing.
        var source = """
            using Pragmatic.Events.Attributes;

            namespace MyApp.Sales;

            [EnableEventOutbox]
            public class SalesBoundary { }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<Pragmatic.Events.Attributes.EnableEventOutboxAttribute>());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2752").Should().BeTrue();
    }
}
