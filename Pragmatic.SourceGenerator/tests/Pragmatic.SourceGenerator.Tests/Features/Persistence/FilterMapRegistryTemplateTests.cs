using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
/// Template unit tests for FilterMapRegistryTemplate — pure model → output, zero Roslyn.
/// </summary>
public class FilterMapRegistryTemplateTests
{
    [Fact]
    public void RenderOutput_OnlySoftDelete_ContainsSoftDeleteExpression()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true));

        var source = Render(entities);

        source.Should().Contain("SoftDeleteFilters");
        source.Should().Contain("!entity.IsDeleted");
        source.Should().Contain("typeof(global::Contoso.Sales.Invoice)");
        source.Should().NotContain("TenantId");
    }

    [Fact]
    public void RenderOutput_OnlyTenant_ContainsTenantExpression()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", tenant: true));

        var source = Render(entities);

        source.Should().NotContain("SoftDeleteFilters");
        source.Should().Contain("entity.TenantId == tenantId");
        source.Should().Contain("context.SkipTenant");
    }

    [Fact]
    public void RenderOutput_TenantFilter_IsFailClosedOnNullTenant()
    {
        // MT-M2 regression guard. The FilterMap tenant predicate must fail CLOSED on an unresolved
        // tenant: `tenantId != null && ...` yields a constant-false predicate → zero rows, matching the
        // SG TenantFilter and the EF named "Tenant" filter. Guarding the whole tenant block out on a
        // null tenant (`context.TenantId is not null`) was fail-OPEN — it left the nav/Include layer
        // unfiltered when the root guard was bypassed (Raw/IgnoreGlobalFilters).
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", tenant: true));

        var source = Render(entities);

        source.Should().Contain(
            "entity => tenantId != null && entity.TenantId == tenantId",
            "the FilterMap tenant predicate must fail closed on an unresolved tenant (MT-M2)");
        source.Should().NotContain(
            "context.TenantId is not null",
            "guarding the tenant block out on a null tenant was fail-open (MT-M2)");
    }

    [Fact]
    public void RenderOutput_BothFilters_ContainsBoth()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true, tenant: true));

        var source = Render(entities);

        source.Should().Contain("SoftDeleteFilters");
        source.Should().Contain("!entity.IsDeleted");
        source.Should().Contain("entity.TenantId == tenantId");
    }

    [Fact]
    public void RenderOutput_MultipleEntities_AllIncluded()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true),
            BuildEntity("Contoso.Sales", "LineItem", softDelete: true, tenant: true),
            BuildEntity("Contoso.Guests", "Guest", tenant: true));

        var source = Render(entities);

        source.Should().Contain("typeof(global::Contoso.Sales.Invoice)");
        source.Should().Contain("typeof(global::Contoso.Sales.LineItem)");
        source.Should().Contain("typeof(global::Contoso.Guests.Guest)");
    }

    [Fact]
    public void RenderOutput_NoFilters_GeneratesEmptySource()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice"));

        var template = new FilterMapRegistryTemplate(entities);
        var artifact = template.RenderOutput();

        // Validate() returns false → ToString() returns null → empty SourceText
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void RenderOutput_RawMode_ReturnsEmpty()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true));

        var source = Render(entities);

        source.Should().Contain("context.IsRaw");
        source.Should().Contain("FilterMap.Empty");
    }

    [Fact]
    public void RenderOutput_HintName_UsesNamespacePrefix()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true));

        var template = new FilterMapRegistryTemplate(entities);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Be("_Infra.Persistence.FilterMap.g.cs");
    }

    [Fact]
    public void RenderOutput_IncludesReferenceEntities()
    {
        // FilterMapRegistry is per-host — it must include ALL entities,
        // including those from referenced assemblies
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true, fromReference: true),
            BuildEntity("Contoso.Sales", "Order", softDelete: true));

        var source = Render(entities);

        source.Should().Contain("typeof(global::Contoso.Sales.Invoice)");
        source.Should().Contain("typeof(global::Contoso.Sales.Order)");
    }

    [Fact]
    public void RenderOutput_CreateForContext_IsStaticMethod()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true));

        var source = Render(entities);

        source.Should().Contain("public static");
        source.Should().Contain("CreateForContext");
        source.Should().Contain("FilterContext context");
    }

    private static string Render(ImmutableArray<EntityMetadataModel> entities)
    {
        var template = new FilterMapRegistryTemplate(entities);
        var artifact = template.RenderOutput();
        artifact.IsEmpty.Should().BeFalse();
        return artifact.Text;
    }

    [Fact]
    public void RenderOutput_TemporalEntity_ContainsTemporalFilter()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Auth", "UserRole", temporal: true));

        var source = Render(entities);

        source.Should().Contain("context.Now");
        source.Should().Contain("ValidFrom <= now");
        source.Should().Contain("ValidTo == null || entity.ValidTo > now");
        source.Should().Contain("typeof(global::Contoso.Auth.UserRole)");
    }

    [Fact]
    public void RenderOutput_AllFilterTypes_ContainsAll()
    {
        var entities = ImmutableArray.Create(
            BuildEntity("Contoso.Sales", "Invoice", softDelete: true, tenant: true),
            BuildEntity("Contoso.Auth", "UserRole", temporal: true));

        var source = Render(entities);

        source.Should().Contain("SoftDeleteFilters");
        source.Should().Contain("tenantId");
        source.Should().Contain("context.Now");
    }

    private static EntityMetadataModel BuildEntity(
        string ns, string name,
        bool softDelete = false, bool tenant = false,
        bool fromReference = false, bool temporal = false)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsSoftDelete = softDelete,
            IsTenantEntity = tenant,
            IsTemporalRelation = temporal,
            IsFromReference = fromReference,
            IsValid = true
        };
    }
}
