using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the PatchApplyTemplate which generates ApplyPatch method and _setProperties tracking.
/// </summary>
public class PatchApplyTemplateTests
{
    [Fact]
    public void SimplePatch_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("partial class UpdateOrder");
        source.Should().NotContain("static class"); // Not extension class
    }

    [Fact]
    public void SimplePatch_GeneratesSetPropertiesTracking()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("HashSet<string> _setProperties");
        source.Should().Contain("IReadOnlySet<string> SetProperties");
        source.Should().Contain("void MarkSet(string propertyName)");
    }

    [Fact]
    public void SimplePatch_GeneratesApplyPatchMethod()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("public void ApplyPatch(");
        source.Should().Contain("global::MyApp.Entities.Order target");
    }

    [Fact]
    public void SimplePatch_GeneratesTrackedPropertyAssignment()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Tracked path: _setProperties.Contains check
        source.Should().Contain("_setProperties.Contains(nameof(Name))");
        source.Should().Contain("target.Name = Name");
    }

    [Fact]
    public void SimplePatch_GeneratesNullableFallback()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Nullable fallback path
        source.Should().Contain("if (_setProperties.Count > 0)");
        source.Should().Contain("else");
        source.Should().Contain("if (Name is not null)");
    }

    [Fact]
    public void Patch_WithPrivateSetter_UsesSetMethod()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties =
            [
                new MutationPropertyModel
                {
                    PropertyName = "Email",
                    PropertyType = "string?",
                    TargetPropertyName = "Email",
                    EntityPropertyExists = true,
                    EntityHasPrivateSetter = true,
                    IsNullable = true
                }
            ]
        };

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should use SetEmail() instead of direct assignment (! for null-forgiving)
        source.Should().Contain("target.SetEmail(Email!)");
        source.Should().NotContain("target.Email =");
    }

    [Fact]
    public void Patch_WithNullableValueType_UnwrapsValue()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties =
            [
                new MutationPropertyModel
                {
                    PropertyName = "StarRating",
                    PropertyType = "int?",
                    TargetPropertyName = "StarRating",
                    EntityPropertyExists = true,
                    EntityHasPrivateSetter = false,
                    EntityPropertyType = "int",
                    IsNullable = true,
                    IsValueType = true
                }
            ]
        };

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should unwrap .Value for nullable value type → non-nullable entity (! for null-forgiving)
        source.Should().Contain("StarRating!.Value");
    }

    [Fact]
    public void Patch_WithIgnoredProperty_SkipsProperty()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties =
            [
                new MutationPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    TargetPropertyName = "Name",
                    EntityPropertyExists = true,
                    IsNullable = true
                },
                new MutationPropertyModel
                {
                    PropertyName = "UpdatedBy",
                    PropertyType = "string?",
                    TargetPropertyName = "UpdatedBy",
                    EntityPropertyExists = false,
                    IsIgnored = true,
                    IsNullable = true
                }
            ]
        };

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Name");
        source.Should().NotContain("UpdatedBy");
    }

    [Fact]
    public void Patch_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("UpdateOrder");
        artifact.HintName.Should().Contain("Patch");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void Patch_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.DTOs;");
    }

    [Fact]
    public void Patch_GeneratesXmlDocumentation()
    {
        // Arrange
        var model = CreateModelWithProperties();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("/// <summary>");
        source.Should().Contain("/// <param name=\"target\">");
    }

    [Fact]
    public void Patch_WithInternalAccessibility_GeneratesInternalClass()
    {
        // Arrange
        var model = CreateModelWithProperties() with
        {
            Accessibility = "internal"
        };

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("internal partial class UpdateOrder");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        // Arrange
        var model = new MutationMetadataModel
        {
            Namespace = "Test",
            TypeName = "", // Invalid - empty type name
            Accessibility = "public",
            EntityFullTypeName = "Test.Entity",
            EntityTypeName = ""
        };

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert - When Validate() fails, ToSourceText() returns empty SourceText (not null)
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Patch_NoApplicableProperties_GeneratesComment()
    {
        // Arrange - model with no properties
        var model = CreateBasicModel();

        // Act
        var template = new PatchApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("No applicable properties to patch");
    }

    private static MutationMetadataModel CreateBasicModel()
    {
        return new MutationMetadataModel
        {
            Namespace = "MyApp.DTOs",
            TypeName = "UpdateOrder",
            Accessibility = "public",
            EntityFullTypeName = "MyApp.Entities.Order",
            EntityTypeName = "Order",
            EntityIdType = "System.Guid",
            IdPropertyOnDto = null,
            RequiredIncludes = ImmutableArray<string>.Empty
        };
    }

    private static MutationMetadataModel CreateModelWithProperties()
    {
        return CreateBasicModel() with
        {
            Properties =
            [
                new MutationPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    TargetPropertyName = "Name",
                    EntityPropertyExists = true,
                    EntityHasPrivateSetter = false,
                    IsNullable = true
                },
                new MutationPropertyModel
                {
                    PropertyName = "Total",
                    PropertyType = "decimal?",
                    TargetPropertyName = "Total",
                    EntityPropertyExists = true,
                    EntityHasPrivateSetter = false,
                    EntityPropertyType = "decimal",
                    IsNullable = true,
                    IsValueType = true
                }
            ]
        };
    }
}
