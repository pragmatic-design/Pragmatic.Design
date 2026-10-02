using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Tenant entities are filtered by TenantId on every query (the
///     named "Tenant" global filter + the runtime TenantFilter), so the generated EntityConfiguration
///     emits an index on TenantId. Non-tenant entities do not.
/// </summary>
public class TenantIndexConfigTests
{
    [Fact]
    public void TenantEntity_EmitsTenantIdIndex()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "Sales.Order",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
            IsTenantEntity = true,
        };

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => e.TenantId);");
    }

    [Fact]
    public void NonTenantEntity_DoesNotEmitTenantIdIndex()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "Sales.Order",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
            IsTenantEntity = false,
        };

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().NotContain("e.TenantId");
    }
}
