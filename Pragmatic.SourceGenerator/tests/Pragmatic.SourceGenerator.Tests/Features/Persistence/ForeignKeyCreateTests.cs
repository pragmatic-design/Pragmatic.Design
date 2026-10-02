using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class ForeignKeyCreateTests
{
    [Fact]
    public void Create_WithFkAndNavigation_ExcludesFkFromParameters()
    {
        // When an entity has PropertyId (FK) AND Property (navigation),
        // the FK should be excluded from Create() parameters because
        // EF Core resolves it via navigation fixup.
        var model = BuildModel("Sales", "OrderLine",
            new PropertyMetadataModel
            {
                Name = "Description",
                TypeName = "string",
                IsRequiredForCreate = true
            },
            new PropertyMetadataModel
            {
                Name = "OrderId",
                TypeName = "Guid",
                IsRequiredForCreate = false // FK with matching navigation → excluded by transform
            });

        var source = Render(model);

        source.Should().Contain("string description");
        source.Should().Contain("Description = description,");
        source.Should().NotContain("Guid orderId");
        source.Should().NotContain("OrderId = orderId");
    }

    [Fact]
    public void Create_WithoutNavigation_IncludesFkInParameters()
    {
        // When an entity has PropertyId but NO matching Property navigation,
        // the FK IS required for Create() — the transform marks it as required.
        var model = BuildModel("Sales", "OrderLine",
            new PropertyMetadataModel
            {
                Name = "Description",
                TypeName = "string",
                IsRequiredForCreate = true
            },
            new PropertyMetadataModel
            {
                Name = "OrderId",
                TypeName = "Guid",
                IsRequiredForCreate = true // No matching navigation → included
            });

        var source = Render(model);

        source.Should().Contain("string description");
        source.Should().Contain("global::System.Guid orderId");
        source.Should().Contain("OrderId = orderId,");
    }

    [Fact]
    public void Create_WithNullableFk_ExcludesFkFromParameters()
    {
        // Nullable FK properties are already excluded from Create() parameters
        // because IsRequiredForCreate = false for nullable types.
        var model = BuildModel("Sales", "OrderLine",
            new PropertyMetadataModel
            {
                Name = "Description",
                TypeName = "string",
                IsRequiredForCreate = true
            },
            new PropertyMetadataModel
            {
                Name = "CategoryId",
                TypeName = "Guid?",
                IsNullable = true,
                IsRequiredForCreate = false // Nullable → not required
            });

        var source = Render(model);

        source.Should().Contain("string description");
        source.Should().NotContain("categoryId");
        source.Should().NotContain("CategoryId =");
    }

    [Fact]
    public void Create_MultipleFks_MixedNavigation_HandlesCorrectly()
    {
        // One FK has navigation (excluded), another doesn't (included)
        var model = BuildModel("Sales", "OrderLine",
            new PropertyMetadataModel
            {
                Name = "ProductName",
                TypeName = "string",
                IsRequiredForCreate = true
            },
            new PropertyMetadataModel
            {
                Name = "OrderId",
                TypeName = "Guid",
                IsRequiredForCreate = false // Has Order navigation → excluded
            },
            new PropertyMetadataModel
            {
                Name = "ExternalCategoryId",
                TypeName = "Guid",
                IsRequiredForCreate = true // No navigation → included
            });

        var source = Render(model);

        source.Should().Contain("string productName");
        source.Should().Contain("global::System.Guid externalCategoryId");
        source.Should().NotContain("Guid orderId");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new EntityCreateTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildModel(string ns, string name,
        params PropertyMetadataModel[] properties)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            Properties = properties.ToImmutableArray()
        };
    }
}
