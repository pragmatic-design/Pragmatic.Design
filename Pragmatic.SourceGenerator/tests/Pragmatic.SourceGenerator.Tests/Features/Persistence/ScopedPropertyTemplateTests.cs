using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class ScopedPropertyTemplateTests
{
    [Fact]
    public void ScopedEntity_GeneratesAccessScopesProperty()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var source = Render(model);

        source.Should().Contain("public global::System.Collections.Generic.List<string> AccessScopes { get; private set; } = [];");
    }

    [Fact]
    public void ScopedEntity_GeneratesGrantScopeMethod()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var source = Render(model);

        source.Should().Contain("internal void GrantScope(string scope)");
        source.Should().Contain("AccessScopes.Add(scope)");
    }

    [Fact]
    public void ScopedEntity_GeneratesRevokeScopeMethod()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var source = Render(model);

        source.Should().Contain("internal void RevokeScope(string scope)");
        source.Should().Contain("AccessScopes.Remove(scope)");
    }

    [Fact]
    public void ScopedEntity_ImplementsIScopedEntityInterface()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var source = Render(model);

        source.Should().Contain("global::Pragmatic.Persistence.Entity.IScopedEntity");
    }

    [Fact]
    public void ScopedEntity_WithManualAccessScopes_SkipsGeneration()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Billing.Invoice",
            Namespace = "Billing",
            IdType = "Guid",
            IsValid = true,
            IsScopedEntity = true,
            HasManualScopedEntityProps = true
        };

        var template = new ScopedPropertyTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void NonScopedEntity_SkipsGeneration()
    {
        var model = BuildModel("Sales", "Order", isScopedEntity: false);

        var template = new ScopedPropertyTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void ScopedEntity_HintName_FollowsConvention()
    {
        var model = BuildModel("Billing", "Invoice", isScopedEntity: true);

        var template = new ScopedPropertyTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Invoice");
        artifact.HintName.Should().Contain("Scoping");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new ScopedPropertyTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildModel(string ns, string name, bool isScopedEntity = false)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            IsScopedEntity = isScopedEntity
        };
    }
}
