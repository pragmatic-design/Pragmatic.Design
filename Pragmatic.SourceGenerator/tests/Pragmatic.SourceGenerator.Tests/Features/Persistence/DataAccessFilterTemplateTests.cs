using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class DataAccessFilterTemplateTests
{
    // =========================================================================
    // ScopedDataFilter (ScopedEntity only)
    // =========================================================================

    [Fact]
    public void ScopedOnly_GeneratesScopedDataFilter()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var template = new ScopedDataFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("public sealed class ScopedDataFilter");
        source.Should().Contain("IPermissionBasedFilter");
        source.Should().Contain("Priority => 250");
    }

    [Fact]
    public void ScopedOnly_FilterUsesAccessScopesExpression()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var source = RenderScoped(model);

        source.Should().Contain("entity.AccessScopes.Any(s => userScopes.Contains(s))");
    }

    [Fact]
    public void ScopedOnly_InjectsIUserScopeResolver()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var source = RenderScoped(model);

        source.Should().Contain("IUserScopeResolver scopeResolver");
    }

    [Fact]
    public void ScopedOnly_NotGeneratedWhenCombinedWithOwned()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Billing.Invoice",
            Namespace = "Billing",
            IdType = "Guid",
            IsValid = true,
            IsScopedEntity = true,
            IsOwnedEntity = true
        };

        var template = new ScopedDataFilterTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    // =========================================================================
    // OwnershipFilter (OwnedEntity only) — NOT generated when combined
    // =========================================================================

    [Fact]
    public void OwnershipFilter_NotGeneratedWhenCombinedWithScoped()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Billing.Invoice",
            Namespace = "Billing",
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            IsScopedEntity = true
        };

        var template = new OwnershipFilterTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    // =========================================================================
    // DataAccessFilter (combined L1+L2)
    // =========================================================================

    [Fact]
    public void Combined_GeneratesDataAccessFilter()
    {
        var model = BuildCombinedModel("Billing", "Invoice");

        var template = new DataAccessFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("public sealed class DataAccessFilter");
        source.Should().Contain("IPermissionBasedFilter");
    }

    [Fact]
    public void Combined_HasOrLogic()
    {
        var model = BuildCombinedModel("Billing", "Invoice");

        var source = RenderCombined(model);

        source.Should().Contain("entity.OwnerId == userId || entity.AccessScopes.Any(s => userScopes.Contains(s))");
    }

    [Fact]
    public void Combined_Priority200()
    {
        var model = BuildCombinedModel("Billing", "Invoice");

        var source = RenderCombined(model);

        source.Should().Contain("Priority => 200");
    }

    [Fact]
    public void Combined_BypassPermission()
    {
        var model = BuildCombinedModel("Billing", "Invoice", boundaryName: "Billing");

        var source = RenderCombined(model);

        source.Should().Contain("billing.invoice.view-all");
    }

    [Fact]
    public void Combined_NotGeneratedWhenOnlyOwned()
    {
        var model = BuildModel("Sales", "Order", isOwnedEntity: true);

        var template = new DataAccessFilterTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Combined_NotGeneratedWhenOnlyScoped()
    {
        var model = BuildModel("Sales", "Order", isScopedEntity: true);

        var template = new DataAccessFilterTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string RenderScoped(EntityMetadataModel model)
    {
        var template = new ScopedDataFilterTemplate(model);
        return template.RenderOutput().Text;
    }

    private static string RenderCombined(EntityMetadataModel model)
    {
        var template = new DataAccessFilterTemplate(model);
        return template.RenderOutput().Text;
    }

    private static EntityMetadataModel BuildModel(string ns, string name,
        bool isOwnedEntity = false, bool isScopedEntity = false)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = isOwnedEntity,
            IsScopedEntity = isScopedEntity
        };
    }

    private static EntityMetadataModel BuildCombinedModel(string ns, string name, string? boundaryName = null)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            IsScopedEntity = true,
            BoundaryName = boundaryName
        };
    }
}
