using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Regression guard for the most security-critical generated artifact — the per-entity
///     <c>TenantFilter</c> (<c>IQueryFilter&lt;T&gt;</c>). It MUST fail CLOSED: an unresolved tenant
///     (<c>TenantId == null</c>) matches NO rows. Without this pin, a regression that dropped the
///     <c>TenantId != null</c> guard would silently turn tenant isolation fail-OPEN (cross-tenant leak)
///     and still pass CI.
/// </summary>
public class TenantFilterTemplateTests
{
    [Fact]
    public void TenantEntity_TenantFilter_IsFailClosed()
    {
        var source = new TenantFilterTemplate(BuildEntity(isTenant: true)).RenderOutput().Text;

        source.Should().Contain(
            "entity => tenantContext.TenantId != null && entity.TenantId == tenantContext.TenantId",
            "an unresolved tenant must match no rows — dropping the null guard is a cross-tenant leak");
    }

    [Fact]
    public void TenantEntity_TenantFilter_ImplementsQueryFilterContracts()
    {
        var source = new TenantFilterTemplate(BuildEntity(isTenant: true)).RenderOutput().Text;

        source.Should().Contain("IQueryFilter");
        source.Should().Contain("ITenantFilter");
        source.Should().Contain("200", "the tenant filter runs at priority 200 (after SoftDelete)");
    }

    [Fact]
    public void NonTenantEntity_EmitsNoTenantFilter()
    {
        var source = new TenantFilterTemplate(BuildEntity(isTenant: false)).RenderOutput().Text;

        // Validate() is false for a non-tenant entity → no TenantFilter class is emitted.
        source.Should().NotContain("class TenantFilter");
    }

    private static EntityMetadataModel BuildEntity(bool isTenant)
        => new()
        {
            TypeName = "Invoice",
            FullTypeName = "Contoso.Sales.Invoice",
            Namespace = "Contoso.Sales",
            IdType = "Guid",
            IsValid = true,
            IsTenantEntity = isTenant,
        };
}
