using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the GridFilterApplyTemplate which generates grid filtering with dynamic operator support.
/// </summary>
public class GridFilterApplyTemplateTests
{
    [Fact]
    public void SimpleFilter_GeneratesApplyMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("Apply");
    }

    [Fact]
    public void SimpleFilter_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("partial class OrderGridFilter");
    }

    [Fact]
    public void Filter_WithStringProperty_GeneratesStringFilter()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Name");
    }

    [Fact]
    public void Filter_WithNumericProperty_GeneratesComparison()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Amount",
                    PropertyType = "decimal",
                    IsFilterable = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Amount");
    }

    [Fact]
    public void Filter_WithBoolProperty_GeneratesEquals()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "IsActive",
                    PropertyType = "bool",
                    IsFilterable = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IsActive");
    }

    [Fact]
    public void Filter_WithNullableProperty_ConditionalFilter()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string?",
                    IsNullable = true,
                    IsFilterable = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Status");
    }

    [Fact]
    public void Filter_WithSortable_GeneratesOrderBy()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSortable = true,
                    SortPriority = 0
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("OrderBy");
    }

    [Fact]
    public void Filter_WithPaging_GeneratesSkipTake()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Page",
                    PropertyType = "int",
                    IsPageProperty = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "PageSize",
                    PropertyType = "int",
                    IsPageSizeProperty = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Skip");
        source.Should().Contain("Take");
    }

    [Fact]
    public void Filter_WithOperatorProperty_DynamicOperator()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true,
                    HasOperatorProperty = true,
                    OperatorPropertyName = "NameOperator"
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Name");
    }

    [Fact]
    public void Filter_WithMultipleProperties_GeneratesAll()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "Amount",
                    PropertyType = "decimal",
                    IsFilterable = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSortable = true,
                    SortPriority = 0
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Name");
        source.Should().Contain("Amount");
        source.Should().Contain("CreatedAt");
    }

    [Fact]
    public void SimpleFilter_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Filters;");
    }

    [Fact]
    public void SimpleFilter_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("OrderGridFilter");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            TypeName = "",
            EntityTypeFullName = ""
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void RealisticScenario_ProductGridFilter()
    {
        // Arrange - Complex grid filter with multiple property types
        var model = new GridFilterModel
        {
            Namespace = "Contoso.Catalog.Filters",
            TypeName = "ProductGridFilter",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "Contoso.Catalog.Entities.Product",
            EntityTypeName = "Product",
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true,
                    HasOperatorProperty = true,
                    OperatorPropertyName = "NameOperator"
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "Price",
                    PropertyType = "decimal",
                    IsFilterable = true,
                    IsSortable = true,
                    SortPriority = 1
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "IsActive",
                    PropertyType = "bool",
                    IsFilterable = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSortable = true,
                    SortPriority = 0
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "Page",
                    PropertyType = "int",
                    IsPageProperty = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "PageSize",
                    PropertyType = "int",
                    IsPageSizeProperty = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.Catalog.Filters;");
        source.Should().Contain("partial class ProductGridFilter");
        source.Should().Contain("Name");
        source.Should().Contain("Price");
        source.Should().Contain("IsActive");
        source.Should().Contain("OrderBy");
        source.Should().Contain("Skip");
        source.Should().Contain("Take");
    }

    [Fact]
    public void Filter_WithOrGroup_GeneratesOrPredicate()
    {
        // Arrange — Filter with OR group: (Email OR Username)
        var model = CreateBasicModel() with
        {
            FilterGroups = ImmutableArray.Create(
                new FilterGroupModel
                {
                    PropertyName = "Search",
                    Logic = "Or",
                    Properties = ImmutableArray.Create(
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Email",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        },
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Username",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        }
                    )
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("this.Search is not null");
        source.Should().Contain("||", "OR logic should use || operator");
        source.Should().Contain("Email");
        source.Should().Contain("Username");
    }

    [Fact]
    public void Filter_WithAndGroupInsideOrGroup_CombinesCorrectly()
    {
        // Arrange — Two groups: AND group + OR group
        var model = CreateBasicModel() with
        {
            FilterGroups = ImmutableArray.Create(
                new FilterGroupModel
                {
                    PropertyName = "SearchOr",
                    Logic = "Or",
                    Properties = ImmutableArray.Create(
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Email",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        },
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Phone",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        }
                    )
                },
                new FilterGroupModel
                {
                    PropertyName = "DateRange",
                    Logic = "And",
                    Properties = ImmutableArray.Create(
                        new GridFilterPropertyModel
                        {
                            PropertyName = "StartDate",
                            PropertyType = "System.DateTime?",
                            IsFilterable = true,
                            IsNullable = true
                        },
                        new GridFilterPropertyModel
                        {
                            PropertyName = "EndDate",
                            PropertyType = "System.DateTime?",
                            IsFilterable = true,
                            IsNullable = true
                        }
                    )
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // OR group uses ||
        var searchSection = source[source.IndexOf("SearchOr", StringComparison.Ordinal)..source.IndexOf("DateRange", StringComparison.Ordinal)];
        searchSection.Should().Contain("||");

        // AND group uses &&
        var dateSection = source[source.IndexOf("DateRange", StringComparison.Ordinal)..];
        dateSection.Should().Contain("&&");
    }

    [Fact]
    public void Filter_WithQueryComposition_AppliesFiltersAndGroups()
    {
        // Arrange — Combines top-level filters + OR group
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true,
                    IsNullable = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "Page",
                    PropertyType = "int",
                    IsPageProperty = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "PageSize",
                    PropertyType = "int",
                    IsPageSizeProperty = true
                }
            ),
            FilterGroups = ImmutableArray.Create(
                new FilterGroupModel
                {
                    PropertyName = "ContactSearch",
                    Logic = "Or",
                    Properties = ImmutableArray.Create(
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Email",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        },
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Phone",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        }
                    )
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // Top-level filter
        source.Should().Contain("this.Name");
        // OR group
        source.Should().Contain("ContactSearch is not null");
        source.Should().Contain("||");
        // Paging
        source.Should().Contain("Skip");
        source.Should().Contain("Take");
    }

    // --- ToSpecification tests ---

    [Fact]
    public void ToSpecification_BasicFilter_GeneratesMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert
        source.Should().Contain("ToSpecification");
        source.Should().Contain("Pragmatic.Specification.Specification<global::MyApp.Sales.Entities.Order>");
        source.Should().Contain("Pragmatic.Specification.Spec<global::MyApp.Sales.Entities.Order>.True");
    }

    [Fact]
    public void ToSpecification_StringProperty_GeneratesContainsSpec()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true,
                    IsNullable = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert — Spec uses Contains for strings
        source.Should().Contain("Spec<global::MyApp.Sales.Entities.Order>.Where(e => e.Name.Contains(");
    }

    [Fact]
    public void ToSpecification_NumericProperty_GeneratesEqualsSpec()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Amount",
                    PropertyType = "decimal",
                    IsFilterable = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert — Spec uses == for non-strings
        source.Should().Contain("Spec<global::MyApp.Sales.Entities.Order>.Where(e => e.Amount == ");
    }

    [Fact]
    public void ToSpecification_DynamicOperator_GeneratesSwitchExpression()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Price",
                    PropertyType = "decimal",
                    IsFilterable = true,
                    IsNullable = true,
                    HasOperatorProperty = true,
                    OperatorPropertyName = "PriceOperator",
                    AllowedOperators = FilterOpsKind.All
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert — switch expression with operator cases
        source.Should().Contain("this.PriceOperator switch");
        source.Should().Contain("FilterOperator.Equals");
        source.Should().Contain("FilterOperator.GreaterThan");
        source.Should().Contain("FilterOperator.LessThan");
    }

    [Fact]
    public void ToSpecification_StringDynamicOperator_GeneratesStringSwitchExpression()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true,
                    IsNullable = true,
                    HasOperatorProperty = true,
                    OperatorPropertyName = "NameOperator",
                    AllowedOperators = FilterOpsKind.All
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert
        source.Should().Contain("this.NameOperator switch");
        source.Should().Contain("StringOperator.Contains");
        source.Should().Contain("StringOperator.StartsWith");
        source.Should().Contain("StringOperator.EndsWith");
    }

    [Fact]
    public void ToSpecification_OrGroup_GeneratesOrLogic()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray<GridFilterPropertyModel>.Empty,
            FilterGroups = ImmutableArray.Create(
                new FilterGroupModel
                {
                    PropertyName = "Search",
                    Logic = "Or",
                    Properties = ImmutableArray.Create(
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Email",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        },
                        new GridFilterPropertyModel
                        {
                            PropertyName = "Username",
                            PropertyType = "string",
                            IsFilterable = true,
                            IsNullable = true
                        }
                    )
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert — OR group uses Spec.False identity and | operator
        source.Should().Contain("Spec<global::MyApp.Sales.Entities.Order>.False");
        source.Should().Contain("groupSpec |");
    }

    [Fact]
    public void ToSpecification_AndGroup_GeneratesAndLogic()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray<GridFilterPropertyModel>.Empty,
            FilterGroups = ImmutableArray.Create(
                new FilterGroupModel
                {
                    PropertyName = "DateRange",
                    Logic = "And",
                    Properties = ImmutableArray.Create(
                        new GridFilterPropertyModel
                        {
                            PropertyName = "StartDate",
                            PropertyType = "System.DateTime?",
                            IsFilterable = true,
                            IsNullable = true
                        },
                        new GridFilterPropertyModel
                        {
                            PropertyName = "EndDate",
                            PropertyType = "System.DateTime?",
                            IsFilterable = true,
                            IsNullable = true
                        }
                    )
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert — AND group uses Spec.True identity and & operator
        // Note: there are 2 True: one for main spec, one for group
        source.Should().Contain("groupSpec &");
    }

    [Fact]
    public void ToSpecification_NoFilters_NoMethod()
    {
        // Arrange — only sorting/paging, no filterable properties or groups
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSortable = true,
                    SortPriority = 0
                }
            ),
            FilterGroups = ImmutableArray<FilterGroupModel>.Empty
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Assert — no ToSpecification when there are no filters
        source.Should().NotContain("ToSpecification");
    }

    [Fact]
    public void ToSpecification_DoesNotIncludeSortingOrPaging()
    {
        // Arrange — model with filters + sorting + paging
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilterable = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSortable = true,
                    SortPriority = 0
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "Page",
                    PropertyType = "int",
                    IsPageProperty = true
                },
                new GridFilterPropertyModel
                {
                    PropertyName = "PageSize",
                    PropertyType = "int",
                    IsPageSizeProperty = true
                }
            )
        };

        // Act
        var template = new GridFilterApplyTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // Extract the ToSpecification method section
        var specIdx = source.IndexOf("ToSpecification", StringComparison.Ordinal);
        var applyIdx = source.IndexOf("Apply", StringComparison.Ordinal);
        specIdx.Should().BeGreaterThan(applyIdx, "ToSpecification should come after Apply");

        var specSection = source[specIdx..];
        specSection.Should().NotContain("OrderBy");
        specSection.Should().NotContain("Skip");
        specSection.Should().NotContain("Take");
    }

    private static GridFilterModel CreateBasicModel()
    {
        return new GridFilterModel
        {
            Namespace = "MyApp.Sales.Filters",
            TypeName = "OrderGridFilter",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "MyApp.Sales.Entities.Order",
            EntityTypeName = "Order",
            Properties = ImmutableArray.Create(
                new GridFilterPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    IsFilterable = true
                }
            )
        };
    }
}
