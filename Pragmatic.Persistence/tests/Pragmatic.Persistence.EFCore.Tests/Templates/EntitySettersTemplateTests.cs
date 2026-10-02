using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the EntitySettersTemplate which generates internal Set methods for private-setter properties.
/// </summary>
public class EntitySettersTemplateTests
{
    [Fact]
    public void Entity_WithPrivateSetters_GeneratesSetMethods()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("SetStatus");
    }

    [Fact]
    public void Entity_WithMultiplePrivateSetters_GeneratesAll()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Status",
                    TypeName = "string",
                    HasPrivateSetter = true
                },
                new PropertyMetadataModel
                {
                    Name = "UpdatedBy",
                    TypeName = "string",
                    HasPrivateSetter = true
                },
                new PropertyMetadataModel
                {
                    Name = "Name",
                    TypeName = "string",
                    HasPrivateSetter = false
                }
            )
        };

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("SetStatus");
        source.Should().Contain("SetUpdatedBy");
    }

    [Fact]
    public void Entity_WithNoPrivateSetters_ReturnsEmptySource()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Name",
                    TypeName = "string",
                    HasPrivateSetter = false
                }
            )
        };

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void SetMethod_HasCorrectSignature()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("void SetStatus");
        source.Should().Contain("string");
    }

    [Fact]
    public void SetMethod_AssignsProperty()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Status = ");
    }

    [Fact]
    public void SimpleEntity_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("partial class Order");
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Entities;");
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("Order");
        artifact.HintName.Should().Contain("Setters");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        // Arrange
        var model = CreateBasicModel() with { IsValid = false };

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Entity_WithMixedSetters_OnlyGeneratesPrivate()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Status",
                    TypeName = "string",
                    HasPrivateSetter = true
                },
                new PropertyMetadataModel
                {
                    Name = "Name",
                    TypeName = "string",
                    HasPrivateSetter = false
                },
                new PropertyMetadataModel
                {
                    Name = "Description",
                    TypeName = "string",
                    HasPrivateSetter = false
                }
            )
        };

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("SetStatus");
        source.Should().NotContain("SetName");
        source.Should().NotContain("SetDescription");
    }

    // =========================================================================
    // Change Tracking Tests
    // =========================================================================

    [Fact]
    public void Entity_ImplementsIChangeTracking()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IChangeTracking");
    }

    [Fact]
    public void Entity_GeneratesModifiedPropertiesField()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HashSet<string> _modifiedProperties");
        source.Should().Contain("ModifiedProperties => _modifiedProperties");
    }

    [Fact]
    public void Entity_GeneratesCollectionsModifiedField()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HashSet<string> _collectionsModified");
        source.Should().Contain("CollectionsModified => _collectionsModified");
    }

    [Fact]
    public void Entity_GeneratesResetModifiedProperties()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("void ResetModifiedProperties()");
        source.Should().Contain("_modifiedProperties.Clear()");
        source.Should().Contain("_collectionsModified.Clear()");
    }

    [Fact]
    public void Entity_GeneratesIsNewProperty()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("bool IsNew");
        source.Should().Contain("get => _isNew");
        source.Should().Contain("set => _isNew = value");
    }

    [Fact]
    public void SetMethod_TracksModifiedProperty()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("_modifiedProperties.Add(nameof(Status))");
    }

    [Fact]
    public void SetMethod_HasEqualityCheck()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Keyword aliases (string, int, etc.) don't get global:: prefix
        source.Should().Contain("EqualityComparer<string>.Default.Equals(Status, value)");
    }

    [Fact]
    public void SetMethod_SkipsAssignmentWhenValueUnchanged()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // The equality check should lead to a return (skip)
        source.Should().Contain("return;");
    }

    [Fact]
    public void Entity_WithMultiplePrivateSetters_TracksEachProperty()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Status",
                    TypeName = "string",
                    HasPrivateSetter = true
                },
                new PropertyMetadataModel
                {
                    Name = "Email",
                    TypeName = "string",
                    HasPrivateSetter = true
                }
            )
        };

        // Act
        var template = new EntitySettersTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("_modifiedProperties.Add(nameof(Status))");
        source.Should().Contain("_modifiedProperties.Add(nameof(Email))");
    }

    private static EntityMetadataModel CreateBasicModel()
    {
        return new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "Status",
                    TypeName = "string",
                    HasPrivateSetter = true
                }
            )
        };
    }
}
