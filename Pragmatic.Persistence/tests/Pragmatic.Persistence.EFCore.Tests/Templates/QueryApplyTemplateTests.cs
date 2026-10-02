using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the QueryApplyTemplate which generates filtering, sorting, and paging logic for query objects.
/// </summary>
public class QueryApplyTemplateTests
{
    [Fact]
    public void SimpleQuery_GeneratesApplyMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("Apply");
    }

    [Fact]
    public void SimpleQuery_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("partial class GetOrdersQuery");
    }

    [Fact]
    public void Query_WithFilter_GeneratesWhereClause()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Equals
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Where");
        source.Should().Contain("Status");
    }

    [Fact]
    public void Query_WithNullableFilter_GeneratesConditionalWhere()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string?",
                    IsFilter = true,
                    IsNullable = true,
                    Operator = FilterOperatorKind.Equals
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Status");
    }

    [Fact]
    public void Query_WithContainsFilter_GeneratesContains()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Contains
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Contains");
    }

    [Fact]
    public void Query_WithIgnoreCaseFilter_LowersBothSides()
    {
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Plate",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Contains,
                    IgnoreCase = true
                }
            )
        };

        var source = new QueryApplyTemplate(model).RenderOutput().Text;

        // Lowering one side only would still miss AB123CD for "b123c" — the case the attribute
        // promises to handle, and misses silently when the option is not read.
        source.Should().Contain("e.Plate.ToLower().Contains(this.Plate.ToLower())",
            "both sides have to be lowered — lowering one still misses AB123CD for \"b123c\"");
    }

    [Fact]
    public void Query_WithIgnoreCaseOnNullableColumn_GuardsAgainstNull()
    {
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Plate",
                    PropertyType = "string",
                    IsFilter = true,
                    // Equals needs no guard on its own; ToLower() dereferences the column anyway.
                    Operator = FilterOperatorKind.Equals,
                    EntityPathIsNullable = true,
                    IgnoreCase = true
                }
            )
        };

        var source = new QueryApplyTemplate(model).RenderOutput().Text;

        source.Should().Contain("!= null &&", "ToLower() on a null column throws over objects");
    }

    [Fact]
    public void Query_WithMultipleFilters_ChainsWhere()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Equals
                },
                new QueryPropertyModel
                {
                    PropertyName = "CustomerId",
                    PropertyType = "System.Guid",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Equals
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Status");
        source.Should().Contain("CustomerId");
    }

    [Fact]
    public void Query_WithSort_GeneratesOrderBy()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSort = true,
                    DefaultSortDirection = SortDirectionKind.Ascending,
                    SortPriority = 0
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("OrderBy");
    }

    [Fact]
    public void Query_WithSortDescending_GeneratesOrderByDesc()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSort = true,
                    DefaultSortDirection = SortDirectionKind.Descending,
                    SortPriority = 0
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("OrderByDescending");
    }

    [Fact]
    public void Query_WithMultipleSorts_GeneratesThenBy()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "LastName",
                    PropertyType = "string",
                    IsSort = true,
                    DefaultSortDirection = SortDirectionKind.Ascending,
                    SortPriority = 0
                },
                new QueryPropertyModel
                {
                    PropertyName = "FirstName",
                    PropertyType = "string",
                    IsSort = true,
                    DefaultSortDirection = SortDirectionKind.Ascending,
                    SortPriority = 1
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("OrderBy");
        source.Should().Contain("ThenBy");
    }

    [Fact]
    public void Query_WithPaging_DoesNotGenerateSkipTake()
    {
        // Arrange — Paging is handled by IQueryExecutor, not Apply()
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Page",
                    PropertyType = "int",
                    IsPageProperty = true
                },
                new QueryPropertyModel
                {
                    PropertyName = "PageSize",
                    PropertyType = "int",
                    IsPageSizeProperty = true
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert — Apply() should NOT contain paging; IQueryExecutor handles it
        var source = artifact.Text;

        source.Should().NotContain(".Skip(");
        source.Should().NotContain(".Take(");
    }

    [Fact]
    public void Query_WithoutPaging_NoPagingLogic()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Equals
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("Skip");
        source.Should().NotContain("Take");
    }

    [Fact]
    public void Query_WithJoin_GeneratesInclude()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    Via = "Customer",
                    IsCollection = false
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Include");
        source.Should().Contain("Customer");
    }

    [Fact]
    public void Query_WithDifferentEntityResult_GeneratesProjectionProperty()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            ResultTypeFullName = "MyApp.Sales.Dtos.OrderDto",
            ResultTypeName = "OrderDto"
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // When entity != result, generates Projection property delegating to DTO's static Projection
        source.Should().Contain("Projection");
        source.Should().Contain("global::MyApp.Sales.Dtos.OrderDto.Projection");
    }

    [Fact]
    public void Query_WithSameEntityResult_NoProjectionProperty()
    {
        // Arrange
        var model = CreateBasicModel(); // Same entity and result

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        // When entity == result, no Projection property needed
        source.Should().NotContain("Projection");
    }

    [Fact]
    public void Query_WithSameEntityResult_GeneratesApplyMethod()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            ResultTypeFullName = "MyApp.Sales.Entities.Order",
            ResultTypeName = "Order"
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("Apply");
        source.Should().Contain("Order");
    }

    [Fact]
    public void Query_WithStartsWithFilter_GeneratesStartsWith()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Name",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.StartsWith
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("StartsWith");
    }

    [Fact]
    public void Query_WithGreaterThanFilter_GeneratesComparison()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Amount",
                    PropertyType = "decimal",
                    IsFilter = true,
                    Operator = FilterOperatorKind.GreaterThan
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Amount");
    }

    [Fact]
    public void SimpleQuery_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Queries;");
    }

    [Fact]
    public void SimpleQuery_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("GetOrdersQuery");
        artifact.HintName.Should().Contain("Query");
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
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void Query_WithMapToProperty_UsesMapping()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "CustomerName",
                    PropertyType = "string",
                    IsFilter = true,
                    Operator = FilterOperatorKind.Contains,
                    MapTo = "Customer.Name"
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Customer");
        source.Should().Contain("Name");
    }

    [Fact]
    public void RealisticScenario_OrdersQuery()
    {
        // Arrange - Complex query with filters, sorts, paging, and joins
        var model = new QueryModel
        {
            Namespace = "Contoso.Sales.Queries",
            TypeName = "GetOrdersQuery",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "Contoso.Sales.Entities.Order",
            EntityTypeName = "Order",
            ResultTypeFullName = "Contoso.Sales.Entities.Order",
            ResultTypeName = "Order",
            Properties = ImmutableArray.Create(
                new QueryPropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string?",
                    IsFilter = true,
                    IsNullable = true,
                    Operator = FilterOperatorKind.Equals
                },
                new QueryPropertyModel
                {
                    PropertyName = "CustomerName",
                    PropertyType = "string?",
                    IsFilter = true,
                    IsNullable = true,
                    Operator = FilterOperatorKind.Contains,
                    MapTo = "Customer.Name"
                },
                new QueryPropertyModel
                {
                    PropertyName = "CreatedAt",
                    PropertyType = "System.DateTime",
                    IsSort = true,
                    DefaultSortDirection = SortDirectionKind.Descending,
                    SortPriority = 0
                },
                new QueryPropertyModel
                {
                    PropertyName = "Page",
                    PropertyType = "int",
                    IsPageProperty = true
                },
                new QueryPropertyModel
                {
                    PropertyName = "PageSize",
                    PropertyType = "int",
                    IsPageSizeProperty = true
                }
            ),
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "Contoso.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    Via = "Customer",
                    IsCollection = false
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.Sales.Queries;");
        source.Should().Contain("partial class GetOrdersQuery");
        source.Should().Contain("Status");
        source.Should().Contain("Contains");
        source.Should().Contain("OrderByDescending");
        source.Should().Contain("Include");
        // Paging is NOT in Apply() — IQueryExecutor handles it
        source.Should().NotContain(".Skip(");
        source.Should().NotContain(".Take(");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Join Tests
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     A resolved <c>Via</c> is an include <b>path</b>, not an <c>Include</c> inside <c>Apply</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not <c>.Include(e =&gt; e.Customer)</c>: <c>IncludePaths</c> is the channel
    ///     <c>EfCoreQueryExecutor.PrepareSource</c> applies <b>before</b> <c>Apply</c> runs, and an
    ///     <c>Include</c> inside <c>Apply</c> is dropped by EF Core the moment the query projects —
    ///     which is the normal case.
    /// </remarks>
    [Fact]
    public void Query_WithNavigationJoin_ListsThePathInIncludePaths()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    Via = "Customer"
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IncludePaths => [\"Customer\"");
        source.Should().NotContain(".Include(",
            "the executor applies the paths itself; a second Include inside Apply is a duplicate on a "
            + "projecting query and dead weight on every other");
    }

    [Fact]
    public void Query_WithNestedNavigationJoin_GeneratesStringInclude()
    {
        // Arrange — Nested join: Lines.Product
        var model = CreateBasicModel() with
        {
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Product",
                    TargetTypeName = "Product",
                    Via = "Lines.Product"
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Include");
        source.Should().Contain("Lines.Product");
    }

    /// <summary>
    ///     A key join on a query whose result <b>is</b> the entity generates nothing at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This case asserted that the template wrote the key names into a <b>comment</b> — "EF Core
    ///     doesn't support explicit LINQ joins as Include" — and that comment was the whole of the
    ///     feature. A key join now generates a real join, but only where the joined
    ///     columns have somewhere to go: this model answers with <c>Order</c> itself, so there is no
    ///     result type to carry them and the declaration is <c>PRAG0738</c>, not a comment.
    /// </remarks>
    [Fact]
    public void Query_WithKeyJoinAndAnEntityShapedResult_GeneratesNothing()
    {
        // Arrange — FK/PK join on a query that answers with the entity
        var model = CreateBasicModel() with
        {
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    ForeignKey = "CustomerId",
                    TargetKey = "Id"
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("CustomerId",
            "a comment that named the keys and generated no join is not a join");
        source.Should().NotContain(".Join(",
            "there is no result type for the joined columns to reach, which is what PRAG0738 says");
    }

    /// <summary>
    ///     A key join that <b>can</b> deliver its columns generates the step and its binding.
    /// </summary>
    /// <remarks>
    ///     The counterpart of the case above, at the template's own level: a distinct result type and a
    ///     boundary to read the target's set from are what turn the declaration into a join.
    /// </remarks>
    [Fact]
    public void Query_WithKeyJoinAndAResultType_GeneratesTheJoinAndItsBinding()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            ResultTypeFullName = "MyApp.Sales.Dtos.OrderRow",
            ResultTypeName = "OrderRow",
            BoundaryTypeName = "MyApp.Sales.SalesBoundary",
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    ForeignKey = "CustomerId",
                    TargetKey = "Id",
                    ForeignKeyMember = "CustomerId",
                    TargetKeyMember = "PersistenceId",
                    ForeignKeyTypeFullName = "global::System.Guid",
                    TargetKeyTypeFullName = "global::System.Guid"
                }
            ),
            JoinedResultProperties = ImmutableArray.Create(
                new JoinedResultPropertyModel
                {
                    Name = "CustomerName",
                    SourceProperty = "Name",
                    JoinIndex = 0,
                    NeedsNullGuard = false,
                    TypeFullName = "global::System.String"
                }
            )
        };

        // Act
        var source = new QueryApplyTemplate(model).RenderOutput().Text;

        // Assert
        source.Should().Contain("IJoiningQuery");
        source.Should().Contain("ForBoundary<global::MyApp.Sales.SalesBoundary>()",
            "the target's set comes from the ROOT boundary's context: EF Core composes a join only "
            + "inside one DbContext instance");
        source.Should().Contain(".Join(");
        source.Should().Contain("CustomerName = __c.__t0.Name");
    }

    [Fact]
    public void Query_WithMultipleJoins_GeneratesAllIncludes()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    Via = "Customer"
                },
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.OrderLine",
                    TargetTypeName = "OrderLine",
                    Via = "Lines",
                    IsCollection = true
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("IncludePaths => [\"Customer\", \"Lines\"]",
            "both paths are the author's, and they arrive in one list in declaration order");
        source.Should().NotContain(".Include(");
    }

    /// <summary>
    ///     A join adds no EF Core <c>using</c>, because nothing in the file names an EF Core API.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A resolved <c>Via</c> is a string in <c>IncludePaths</c> and a key join is
    ///     <c>Queryable.Join</c> — both plain <c>System.Linq</c>. A <c>using</c> for an
    ///     assembly the file does not use is one more thing a module has to reference to compile what
    ///     was generated for it.
    /// </remarks>
    [Fact]
    public void Query_WithJoin_NeedsNoEntityFrameworkUsing()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            Joins = ImmutableArray.Create(
                new JoinModel
                {
                    TargetTypeFullName = "MyApp.Sales.Entities.Customer",
                    TargetTypeName = "Customer",
                    Via = "Customer"
                }
            )
        };

        // Act
        var template = new QueryApplyTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().NotContain("using Microsoft.EntityFrameworkCore;");
        source.Should().Contain("IncludePaths", "the path is still declared — through the channel that works");
    }

    private static QueryModel CreateBasicModel()
    {
        return new QueryModel
        {
            Namespace = "MyApp.Sales.Queries",
            TypeName = "GetOrdersQuery",
            Accessibility = "public",
            TypeKind = "class",
            EntityTypeFullName = "MyApp.Sales.Entities.Order",
            EntityTypeName = "Order",
            ResultTypeFullName = "MyApp.Sales.Entities.Order",
            ResultTypeName = "Order"
        };
    }
}
