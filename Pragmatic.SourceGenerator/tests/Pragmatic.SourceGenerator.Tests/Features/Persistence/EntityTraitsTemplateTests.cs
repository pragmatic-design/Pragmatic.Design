using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class EntityTraitsTemplateTests
{
    [Fact]
    public void Entity_WithAuditable_GeneratesAuditableTraitProperties()
    {
        var model = BuildModel("Sales", "Order", isAuditable: true);

        var source = Render(model);

        source.Should().Contain("CreatedAt");
        source.Should().Contain("CreatedBy");
        source.Should().Contain("UpdatedAt");
        source.Should().Contain("UpdatedBy");
        source.Should().Contain("IAuditable");
    }

    [Fact]
    public void Entity_WithSoftDelete_GeneratesSoftDeleteTraitProperties()
    {
        var model = BuildModel("Sales", "Order", isSoftDelete: true);

        var source = Render(model);

        source.Should().Contain("IsDeleted");
        source.Should().Contain("DeletedAt");
        source.Should().Contain("DeletedBy");
        source.Should().Contain("ISoftDelete");
    }

    [Fact]
    public void Entity_WithBoth_GeneratesAllTraitProperties()
    {
        var model = BuildModel("Sales", "Order", isAuditable: true, isSoftDelete: true);

        var source = Render(model);

        // Auditable
        source.Should().Contain("CreatedAt");
        source.Should().Contain("CreatedBy");
        source.Should().Contain("UpdatedAt");
        source.Should().Contain("UpdatedBy");
        source.Should().Contain("IAuditable");

        // SoftDelete
        source.Should().Contain("IsDeleted");
        source.Should().Contain("DeletedAt");
        source.Should().Contain("DeletedBy");
        source.Should().Contain("ISoftDelete");
    }

    [Fact]
    public void Entity_WithManualProperties_SkipsGeneration()
    {
        // When the developer has already declared PersistenceId, Auditable, and SoftDelete manually
        var model = new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "Sales.Order",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
            IsAuditable = true,
            IsSoftDelete = true,
            HasManualPersistenceId = true,
            HasManualAuditableProps = true,
            HasManualSoftDeleteProps = true,
            HasManualAuditableInterface = true,
            HasManualSoftDeleteInterface = true
        };

        // NeedsTraitGeneration should be false, so Validate() returns false
        model.NeedsTraitGeneration.Should().BeFalse();

        var template = new EntityTraitsTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Entity_NoTraits_GeneratesPersistenceIdOnly()
    {
        // Plain [Entity] without Auditable/SoftDelete
        // Still needs PersistenceId + Id generation
        var model = BuildModel("Sales", "Order");

        var source = Render(model);

        source.Should().Contain("PersistenceId");
        source.Should().Contain("Id => PersistenceId");
        source.Should().NotContain("CreatedAt");
        source.Should().NotContain("IsDeleted");
        source.Should().NotContain("IAuditable");
        source.Should().NotContain("ISoftDelete");
    }

    [Fact]
    public void Entity_WithGuidId_GeneratesGuidV7Default()
    {
        var model = BuildModel("Sales", "Order");

        var source = Render(model);

        source.Should().Contain("Guid.CreateVersion7()");
    }

    [Fact]
    public void Entity_WithManualAuditableInterface_DoesNotAddInterfaceAgain()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "Sales.Order",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
            IsAuditable = true,
            HasManualAuditableInterface = true // Already declares : IAuditable
        };

        var source = Render(model);

        // Should generate properties but NOT add IAuditable to the partial class declaration
        source.Should().Contain("CreatedAt");
        // Count occurrences of IAuditable — should not appear as interface on the class
        // (it's only in the comment)
        source.Should().NotContain(": global::Pragmatic.Persistence.Entity.IAuditable");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new EntityTraitsTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    // The base of a hierarchy is what carries [Entity], so it already declares PersistenceId and
    // Id. Emitting them again on the derived type hides the base's members (CS0108) and gives the
    // derived type a second identity of its own.
    [Fact]
    public void RenderOutput_DerivedEntity_DoesNotRedeclareTheIdentityItsBaseAlreadyHas()
    {
        var derived = BuildModel("Sales", "RushOrder") with { BaseEntityFullTypeName = "Sales.Order" };

        var source = new EntityTraitsTemplate(derived).RenderOutput().Text;

        source.Should().NotContain("PersistenceId");
        source.Should().NotContain("public Guid Id");
    }

    [Fact]
    public void RenderOutput_RootEntity_StillDeclaresItsIdentity()
    {
        var source = new EntityTraitsTemplate(BuildModel("Sales", "Order")).RenderOutput().Text;

        source.Should().Contain("PersistenceId");
    }

    private static EntityMetadataModel BuildModel(string ns, string name,
        bool isAuditable = false, bool isSoftDelete = false)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            IsAuditable = isAuditable,
            IsSoftDelete = isSoftDelete
        };
    }
}
