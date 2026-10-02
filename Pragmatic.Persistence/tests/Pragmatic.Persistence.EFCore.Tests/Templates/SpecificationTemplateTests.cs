using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the SpecificationTemplate which generates static specification classes with ById and ByLogicKey methods.
/// </summary>
public class SpecificationTemplateTests
{
    [Fact]
    public void SimpleEntity_GeneratesSpecificationsClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("OrderSpecifications");
        source.Should().Contain("static");
    }

    /// <summary>
    ///     The generated container is <c>partial</c>, so an application's own specifications live in it.
    /// </summary>
    /// <remarks>
    ///     It was not, and the consequence was two containers that never met: <c>ById</c> and
    ///     <c>ByLogicKey</c> in a generated <c>{Entity}Specifications</c> that — measured across the
    ///     Showcase and the consumer application — <b>nothing referenced and no skill mentioned</b>, and
    ///     the specifications people actually write in a separate <c>{Entity}Specs</c> beside it. One
    ///     word makes them the same class, which is the only way the second author finds the first.
    /// </remarks>
    [Fact]
    public void TheSpecificationsClass_IsPartial()
    {
        new SpecificationTemplate(CreateBasicModel()).RenderOutput().Text
            .Should().Contain("public static partial class OrderSpecifications",
                "an application must be able to add its own specifications to the same container");
    }

    [Fact]
    public void Entity_GeneratesByIdSpecification()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("ById");
        source.Should().Contain("PersistenceId");
    }

    [Fact]
    public void ById_ReturnsExpression()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Specification<");
        source.Should().Contain("Spec<");
    }

    [Fact]
    public void ById_GeneratesQueryableExtension()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IQueryable<");
        source.Should().Contain("this global::System.Linq.IQueryable");
    }

    [Fact]
    public void Entity_WithLogicKey_GeneratesByLogicKeySpec()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = [new LogicKeyPart { Name = "OrderNumber", TypeName = "string" }]
        };

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("ByOrderNumber");
        source.Should().Contain("OrderNumber");
    }

    [Fact]
    public void Entity_WithoutLogicKey_NoLogicKeySpec()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = []
        };

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should only have ById, not ByLogicKey
        source.Should().Contain("ById");
        source.Should().NotContain("ByLogicKey");
    }

    [Fact]
    public void Entity_WithStringLogicKey_CorrectType()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = [new LogicKeyPart { Name = "Code", TypeName = "string" }]
        };

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("ByCode");
        source.Should().Contain("string");
    }

    [Fact]
    public void Entity_WithGuidLogicKey_CorrectType()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            LogicKeys = [new LogicKeyPart { Name = "ExternalId", TypeName = "System.Guid" }]
        };

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("ByExternalId");
        source.Should().Contain("Guid");
    }

    [Fact]
    public void SimpleEntity_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new SpecificationTemplate(model);
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
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("Order");
        artifact.HintName.Should().Contain("Specs");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        // Arrange
        var model = CreateBasicModel() with { IsValid = false };

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void SimpleEntity_GeneratesAutoGeneratedHeader()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("<auto-generated");
    }

    [Fact]
    public void RealisticScenario_CourseEntity()
    {
        // Arrange - Course with CourseCode as logic key
        var model = new EntityMetadataModel
        {
            TypeName = "Course",
            FullTypeName = "Contoso.University.Catalog.Entities.Course",
            Namespace = "Contoso.University.Catalog.Entities",
            IdType = "System.Guid",
            LogicKeys = [new LogicKeyPart { Name = "CourseCode", TypeName = "string" }],
            BoundaryName = "Catalog",
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "CourseCode",
                    TypeName = "string",
                    IsLogicKey = true,
                    IsRequiredForCreate = true
                },
                new PropertyMetadataModel
                {
                    Name = "Title",
                    TypeName = "string",
                    IsRequiredForCreate = true
                }
            )
        };

        // Act
        var template = new SpecificationTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.University.Catalog.Entities;");
        source.Should().Contain("CourseSpecifications");
        source.Should().Contain("ById");
        source.Should().Contain("ByCourseCode");
        source.Should().Contain("PersistenceId");
    }

    private static EntityMetadataModel CreateBasicModel()
    {
        return new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "MyApp.Sales.Entities.Order",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            BoundaryName = "Sales"
        };
    }
}
