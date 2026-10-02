using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for FilterDtoApplyTemplate which generates ApplyFilter extension
///     methods and ToSpecification helpers with AND/OR group support.
/// </summary>
public class FilterDtoApplyTemplateTests
{
    [Fact]
    public void SimpleFilter_GeneratesApplyFilterExtensionMethod()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("ApplyFilter");
        source.Should().Contain("static");
        source.Should().Contain("IQueryable");
    }

    [Fact]
    public void SimpleFilter_GeneratesToSpecificationMethod()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("ToSpecification");
        source.Should().Contain("Specification<");
    }

    [Fact]
    public void SimpleFilter_GeneratesStaticExtensionClass()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("static partial class OrderFilterExtensions");
    }

    [Fact]
    public void SimpleFilter_GeneratesCorrectNamespace()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Filters;");
    }

    [Fact]
    public void SimpleFilter_GeneratesCorrectHintName()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("OrderFilter");
        artifact.HintName.Should().Contain("FilterDto");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void Filter_NullGuard_ApplyFilterReturnsQueryWhenNull()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("if (filter is null) return query;");
    }

    [Fact]
    public void Filter_StringProperty_ContainsOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    EntityPropertyPath = "Name",
                    Operator = "Contains",
                    IsNullable = true,
                    IsString = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Name.Contains(filter.Name)");
    }

    [Fact]
    public void Filter_StringProperty_StartsWithOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    EntityPropertyPath = "Name",
                    Operator = "StartsWith",
                    IsNullable = true,
                    IsString = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Name.StartsWith(filter.Name)");
    }

    [Fact]
    public void Filter_StringProperty_EndsWithOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Email",
                    PropertyType = "string?",
                    EntityPropertyPath = "Email",
                    Operator = "EndsWith",
                    IsNullable = true,
                    IsString = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Email.EndsWith(filter.Email)");
    }

    [Fact]
    public void Filter_EqualsOperator_GeneratesEquality()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "int?",
                    EntityPropertyPath = "Status",
                    Operator = "Equals",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Status == filter.Status.Value");
    }

    [Fact]
    public void Filter_NotEqualsOperator_GeneratesInequality()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "int?",
                    EntityPropertyPath = "Status",
                    Operator = "NotEquals",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Status != filter.Status.Value");
    }

    [Fact]
    public void Filter_GreaterThanOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "MinAmount",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "Amount",
                    Operator = "GreaterThan",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Amount > filter.MinAmount.Value");
    }

    [Fact]
    public void Filter_LessThanOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "MaxAmount",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "Amount",
                    Operator = "LessThan",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Amount < filter.MaxAmount.Value");
    }

    [Fact]
    public void Filter_GreaterOrEqualOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "MinPrice",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "Price",
                    Operator = "GreaterOrEqual",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Price >= filter.MinPrice.Value");
    }

    [Fact]
    public void Filter_LessOrEqualOperator()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "MaxPrice",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "Price",
                    Operator = "LessOrEqual",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Price <= filter.MaxPrice.Value");
    }

    [Fact]
    public void Filter_InOperator_CollectionContains()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "StatusIds",
                    PropertyType = "List<int>?",
                    EntityPropertyPath = "StatusId",
                    Operator = "In",
                    IsNullable = true,
                    IsCollection = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("filter.StatusIds.Contains(e.StatusId)");
    }

    [Fact]
    public void Filter_IgnoreCase_AppliesLowerCase()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    EntityPropertyPath = "Name",
                    Operator = "Contains",
                    IgnoreCase = true,
                    IsNullable = true,
                    IsString = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Name.ToLower().Contains(filter.Name.ToLower())");
    }

    [Fact]
    public void Filter_MapTo_UsesEntityPropertyPath()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "CustomerName",
                    PropertyType = "string?",
                    EntityPropertyPath = "Customer.Name",
                    Operator = "Contains",
                    IsNullable = true,
                    IsString = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("e.Customer.Name.Contains(filter.CustomerName)");
    }

    [Fact]
    public void Filter_NullableValueType_AccessesValueProperty()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "MinDate",
                    PropertyType = "DateTime?",
                    EntityPropertyPath = "CreatedAt",
                    Operator = "GreaterOrEqual",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        // Nullable value types should access .Value
        source.Should().Contain("filter.MinDate.Value");
        // Should have null guard
        source.Should().Contain("if (filter.MinDate is not null)");
    }

    [Fact]
    public void Filter_NullableString_DoesNotAccessValue()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    EntityPropertyPath = "Name",
                    Operator = "Contains",
                    IsNullable = true,
                    IsString = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        // Strings don't need .Value
        source.Should().NotContain("filter.Name.Value");
        source.Should().Contain("filter.Name)");
    }

    [Fact]
    public void FilterGroup_NullableGroup_GeneratesNullGuardAndBuildExpression()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray<FilterDtoPropertyModel>.Empty,
            Groups = ImmutableArray.Create(
                new FilterDtoGroupModel
                {
                    PropertyName = "DateRange",
                    GroupTypeFullName = "MyApp.Sales.Filters.DateRangeFilter",
                    GroupTypeName = "DateRangeFilter",
                    UseOrLogic = false,
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("if (filter.DateRange is not null)");
        source.Should().Contain("DateRangeFilterExtensions.ToSpecification(filter.DateRange");
    }

    [Fact]
    public void FilterGroup_OrLogic_PassesTrueForUseOrLogic()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray<FilterDtoPropertyModel>.Empty,
            Groups = ImmutableArray.Create(
                new FilterDtoGroupModel
                {
                    PropertyName = "OrConditions",
                    GroupTypeFullName = "MyApp.Sales.Filters.OrConditionsFilter",
                    GroupTypeName = "OrConditionsFilter",
                    UseOrLogic = true,
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("useOrLogic: true");
    }

    [Fact]
    public void FilterGroup_AndLogic_PassesFalseForUseOrLogic()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray<FilterDtoPropertyModel>.Empty,
            Groups = ImmutableArray.Create(
                new FilterDtoGroupModel
                {
                    PropertyName = "Conditions",
                    GroupTypeFullName = "MyApp.Sales.Filters.ConditionsFilter",
                    GroupTypeName = "ConditionsFilter",
                    UseOrLogic = false,
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("useOrLogic: false");
    }

    [Fact]
    public void FilterGroup_NonNullable_NoNullGuard()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray<FilterDtoPropertyModel>.Empty,
            Groups = ImmutableArray.Create(
                new FilterDtoGroupModel
                {
                    PropertyName = "DateRange",
                    GroupTypeFullName = "MyApp.Sales.Filters.DateRangeFilter",
                    GroupTypeName = "DateRangeFilter",
                    UseOrLogic = false,
                    IsNullable = false
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        // Non-nullable: no "if (filter.DateRange is not null)" wrapping
        source.Should().Contain("DateRangeFilterExtensions.ToSpecification(filter.DateRange");
        source.Should().Contain("dateRangeSpec");
    }

    [Fact]
    public void MultipleFilters_GeneratesPredicateBuilderCombination()
    {
        var model = CreateBasicModel() with
        {
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string?",
                    EntityPropertyPath = "Name",
                    Operator = "Contains",
                    IsNullable = true,
                    IsString = true
                },
                new FilterDtoPropertyModel
                {
                    PropertyName = "MinAmount",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "Amount",
                    Operator = "GreaterOrEqual",
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("(result & f)");
        source.Should().Contain("(result | f)");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        var model = CreateBasicModel() with
        {
            TypeName = "",
            EntityTypeFullName = ""
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void GeneratorHeader_ContainsGeneratorInfo()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("// Pragmatic.SourceGenerator/Persistence");
        source.Should().Contain("[FilterDto] on OrderFilter");
    }

    [Fact]
    public void RealisticScenario_OrderFilter()
    {
        var model = new FilterDtoModel
        {
            Namespace = "Contoso.Sales.Filters",
            TypeName = "OrderFilter",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "Contoso.Sales.Entities.Order",
            EntityTypeName = "Order",
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "CustomerName",
                    PropertyType = "string?",
                    EntityPropertyPath = "Customer.Name",
                    Operator = "Contains",
                    IgnoreCase = true,
                    IsNullable = true,
                    IsString = true
                },
                new FilterDtoPropertyModel
                {
                    PropertyName = "MinTotal",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "TotalAmount",
                    Operator = "GreaterOrEqual",
                    IsNullable = true
                },
                new FilterDtoPropertyModel
                {
                    PropertyName = "MaxTotal",
                    PropertyType = "decimal?",
                    EntityPropertyPath = "TotalAmount",
                    Operator = "LessOrEqual",
                    IsNullable = true
                },
                new FilterDtoPropertyModel
                {
                    PropertyName = "StatusIds",
                    PropertyType = "List<int>?",
                    EntityPropertyPath = "StatusId",
                    Operator = "In",
                    IsNullable = true,
                    IsCollection = true
                }),
            Groups = ImmutableArray.Create(
                new FilterDtoGroupModel
                {
                    PropertyName = "DateRange",
                    GroupTypeFullName = "Contoso.Sales.Filters.DateRangeFilter",
                    GroupTypeName = "DateRangeFilter",
                    UseOrLogic = false,
                    IsNullable = true
                })
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        // Namespace and class
        source.Should().Contain("namespace Contoso.Sales.Filters;");
        source.Should().Contain("static partial class OrderFilterExtensions");

        // String filter with IgnoreCase
        source.Should().Contain("Customer.Name.ToLower().Contains(filter.CustomerName.ToLower())");

        // Range filters
        source.Should().Contain("e.TotalAmount >= filter.MinTotal.Value");
        source.Should().Contain("e.TotalAmount <= filter.MaxTotal.Value");

        // In filter
        source.Should().Contain("filter.StatusIds.Contains(e.StatusId)");

        // Group
        source.Should().Contain("DateRangeFilterExtensions.ToSpecification");

        // Specification composition
        source.Should().Contain("(result & f)");
    }

    [Fact]
    public void InternalAccessibility_GeneratesInternalClass()
    {
        var model = CreateBasicModel() with
        {
            Accessibility = "internal"
        };

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("internal static partial class OrderFilterExtensions");
    }

    [Fact]
    public void UsingsIncluded()
    {
        var model = CreateBasicModel();

        var template = new FilterDtoApplyTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;

        source.Should().Contain("using System;");
        source.Should().Contain("using System.Linq;");
    }

    private static FilterDtoModel CreateBasicModel()
    {
        return new FilterDtoModel
        {
            Namespace = "MyApp.Sales.Filters",
            TypeName = "OrderFilter",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "MyApp.Sales.Entities.Order",
            EntityTypeName = "Order",
            Filters = ImmutableArray.Create(
                new FilterDtoPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string?",
                    EntityPropertyPath = "Status",
                    Operator = "Equals",
                    IsNullable = true,
                    IsString = true
                }),
            Groups = ImmutableArray<FilterDtoGroupModel>.Empty
        };
    }
}
