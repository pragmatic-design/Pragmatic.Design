using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the GridAdapterTemplate which generates compile-time grid adapters for DevExpress and PrimeNG.
/// </summary>
public class GridAdapterTemplateTests
{
    [Fact]
    public void SimpleAdapter_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("partial class OrderGridAdapter");
    }

    [Fact]
    public void Adapter_WithDevExpress_GeneratesDevExpressAdapter()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Framework = GridFrameworkFlags.DevExpress
        };

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Apply");
        source.Should().Contain("Order");
    }

    [Fact]
    public void Adapter_WithPrimeNG_GeneratesPrimeNGAdapter()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Framework = GridFrameworkFlags.PrimeNG
        };

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Apply");
        source.Should().Contain("Order");
    }

    [Fact]
    public void Adapter_WithBothFrameworks_GeneratesBoth()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Framework = GridFrameworkFlags.Both
        };

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Apply");
    }

    [Fact]
    public void Adapter_WithFields_GeneratesFilterDispatchers()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Should have field-specific filter logic
        source.Should().Contain("Name");
    }

    [Fact]
    public void Adapter_WithSortableField_GeneratesSortDispatcher()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Fields = ImmutableArray.Create(
                new GridFieldModel
                {
                    JsonField = "name",
                    PropertyPath = "Name",
                    PropertyName = "Name",
                    PropertyType = "string",
                    PropertyTypeFullName = "System.String",
                    IsNullable = false,
                    UnderlyingType = "string",
                    UnderlyingTypeFullName = "System.String",
                    TypeCategory = PropertyTypeCategory.String,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = null
                }
            )
        };

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Name");
    }

    [Fact]
    public void Adapter_WithStringField_GeneratesStringOperators()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // String fields should support string operators like Contains
        source.Should().Contain("Name");
    }

    [Fact]
    public void Adapter_WithNumericField_GeneratesNumericOperators()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Fields = ImmutableArray.Create(
                new GridFieldModel
                {
                    JsonField = "amount",
                    PropertyPath = "Amount",
                    PropertyName = "Amount",
                    PropertyType = "decimal",
                    PropertyTypeFullName = "System.Decimal",
                    IsNullable = false,
                    UnderlyingType = "decimal",
                    UnderlyingTypeFullName = "System.Decimal",
                    TypeCategory = PropertyTypeCategory.Numeric,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = null
                }
            )
        };

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Amount");
    }

    [Fact]
    public void SimpleAdapter_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Grid;");
    }

    [Fact]
    public void SimpleAdapter_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("OrderGridAdapter");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void RealisticScenario_ProductGrid()
    {
        // Arrange - Product grid with multiple field types
        var model = new GridAdapterModel
        {
            Namespace = "Contoso.Catalog.Grid",
            TypeName = "ProductGridAdapter",
            Accessibility = "public",
            EntityType = "Product",
            EntityTypeFullName = "Contoso.Catalog.Entities.Product",
            Framework = GridFrameworkFlags.DevExpress,
            SupportNestedFilters = true,
            SupportGrouping = false,
            Fields = ImmutableArray.Create(
                new GridFieldModel
                {
                    JsonField = "name",
                    PropertyPath = "Name",
                    PropertyName = "Name",
                    PropertyType = "string",
                    PropertyTypeFullName = "System.String",
                    IsNullable = false,
                    UnderlyingType = "string",
                    UnderlyingTypeFullName = "System.String",
                    TypeCategory = PropertyTypeCategory.String,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = null
                },
                new GridFieldModel
                {
                    JsonField = "price",
                    PropertyPath = "Price",
                    PropertyName = "Price",
                    PropertyType = "decimal",
                    PropertyTypeFullName = "System.Decimal",
                    IsNullable = false,
                    UnderlyingType = "decimal",
                    UnderlyingTypeFullName = "System.Decimal",
                    TypeCategory = PropertyTypeCategory.Numeric,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = null
                },
                new GridFieldModel
                {
                    JsonField = "isActive",
                    PropertyPath = "IsActive",
                    PropertyName = "IsActive",
                    PropertyType = "bool",
                    PropertyTypeFullName = "System.Boolean",
                    IsNullable = false,
                    UnderlyingType = "bool",
                    UnderlyingTypeFullName = "System.Boolean",
                    TypeCategory = PropertyTypeCategory.Boolean,
                    IsNested = false,
                    Filterable = true,
                    Sortable = false,
                    Groupable = false,
                    AllowedOperators = null
                },
                new GridFieldModel
                {
                    JsonField = "createdAt",
                    PropertyPath = "CreatedAt",
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    PropertyTypeFullName = "System.DateTime",
                    IsNullable = false,
                    UnderlyingType = "System.DateTime",
                    UnderlyingTypeFullName = "System.DateTime",
                    TypeCategory = PropertyTypeCategory.DateTime,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = null
                }
            ),
            ExcludedProperties = ImmutableArray<string>.Empty
        };

        // Act
        var template = new GridAdapterTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.Catalog.Grid;");
        source.Should().Contain("partial class ProductGridAdapter");
        source.Should().Contain("Name");
        source.Should().Contain("Price");
        source.Should().Contain("IsActive");
        source.Should().Contain("CreatedAt");
    }

    [Fact]
    public void Adapter_WithAllowedOperators_EmitsOperatorGuard()
    {
        // [GridField(AllowedOperators=["equals"])] must reach the template: emitting the full operator
        // switch would silently override a dev restricting a field to equals to close the
        // startswith/contains oracle.
        var model = CreateBasicModel() with
        {
            Fields = ImmutableArray.Create(
                new GridFieldModel
                {
                    JsonField = "name",
                    PropertyPath = "Name",
                    PropertyName = "Name",
                    PropertyType = "string",
                    PropertyTypeFullName = "System.String",
                    IsNullable = false,
                    UnderlyingType = "string",
                    UnderlyingTypeFullName = "System.String",
                    TypeCategory = PropertyTypeCategory.String,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = ImmutableArray.Create("equals")
                }
            )
        };

        var template = new GridAdapterTemplate(model);
        var source = template.RenderOutput().Text;

        // The guard rejects any operator outside the (alias-expanded) allow-list before the switch.
        source.Should().Contain("if (op.ToLowerInvariant() is not (\"=\" or \"==\" or \"equals\")) return null;");
        // "startswith"/"contains" are not in the allow-list, so no alias for them is permitted.
        source.Should().NotContain("\"startswith\" or");
    }

    private static GridAdapterModel CreateBasicModel()
    {
        return new GridAdapterModel
        {
            Namespace = "MyApp.Sales.Grid",
            TypeName = "OrderGridAdapter",
            Accessibility = "public",
            EntityType = "Order",
            EntityTypeFullName = "MyApp.Sales.Entities.Order",
            Framework = GridFrameworkFlags.DevExpress,
            SupportNestedFilters = false,
            SupportGrouping = false,
            Fields = ImmutableArray.Create(
                new GridFieldModel
                {
                    JsonField = "name",
                    PropertyPath = "Name",
                    PropertyName = "Name",
                    PropertyType = "string",
                    PropertyTypeFullName = "System.String",
                    IsNullable = false,
                    UnderlyingType = "string",
                    UnderlyingTypeFullName = "System.String",
                    TypeCategory = PropertyTypeCategory.String,
                    IsNested = false,
                    Filterable = true,
                    Sortable = true,
                    Groupable = false,
                    AllowedOperators = null
                }
            ),
            ExcludedProperties = ImmutableArray<string>.Empty
        };
    }
}
