using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Tests for the QueryViewBuildTemplate which generates aggregation and projection logic for query views.
/// </summary>
public class QueryViewBuildTemplateTests
{
    [Fact]
    public void SimpleView_GeneratesBuildMethod()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.IsEmpty.Should().BeFalse();
        var source = artifact.Text;

        source.Should().Contain("Build");
    }

    [Fact]
    public void SimpleView_GeneratesPartialClass()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("partial class OrderSummaryView");
    }

    [Fact]
    public void View_WithGroupBy_GeneratesGroupByClause()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("GroupBy");
    }

    [Fact]
    public void View_WithMultipleGroupBy_GroupsAll()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            GroupByProperties =
            [
                new GroupByModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityProperty = "Status"
                },
                new GroupByModel
                {
                    PropertyName = "CustomerId",
                    PropertyType = "System.Guid",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityProperty = "CustomerId"
                }
            ]
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Status");
        source.Should().Contain("CustomerId");
        source.Should().Contain("GroupBy");
    }

    [Fact]
    public void View_WithSumAggregate_GeneratesSum()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Sum");
    }

    [Fact]
    public void View_WithCountAggregate_GeneratesCount()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "OrderCount",
                    PropertyType = "int",
                    Kind = AggregateKind.Count,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e"
                }
            ]
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Count");
    }

    [Fact]
    public void View_WithAverageAggregate_GeneratesAverage()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "AverageAmount",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Average,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e.Amount"
                }
            ]
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Average");
    }

    [Fact]
    public void View_WithMultipleAggregates_GeneratesAll()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "TotalAmount",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Sum,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e.Amount"
                },
                new AggregatePropertyModel
                {
                    PropertyName = "OrderCount",
                    PropertyType = "int",
                    Kind = AggregateKind.Count,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e"
                },
                new AggregatePropertyModel
                {
                    PropertyName = "MaxAmount",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Max,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e.Amount"
                }
            ]
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Sum");
        source.Should().Contain("Count");
        source.Should().Contain("Max");
    }

    [Fact]
    public void SimpleView_GeneratesCorrectNamespace()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace MyApp.Sales.Views;");
    }

    [Fact]
    public void SimpleView_GeneratesCorrectHintName()
    {
        // Arrange
        var model = CreateBasicModel();

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.HintName.Should().Contain("OrderSummaryView");
        artifact.HintName.Should().EndWith(".g.cs");
    }

    [Fact]
    public void InvalidModel_NoGroupByNoAggregates_ReturnsEmpty()
    {
        // Arrange - IsValid requires GroupBy or Aggregates
        var model = new QueryViewModel
        {
            TypeName = "EmptyView",
            Namespace = "MyApp",
            RootEntityTypeFullName = "MyApp.Order",
            GroupByProperties = [],
            AggregateProperties = []
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void View_WithMinAggregate_GeneratesMin()
    {
        // Arrange
        var model = CreateBasicModel() with
        {
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "MinPrice",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Min,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e.Amount"
                }
            ]
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("Min");
    }

    [Fact]
    public void RealisticScenario_SalesReport()
    {
        // Arrange - Sales report with GroupBy status and aggregates
        var model = new QueryViewModel
        {
            TypeName = "SalesReportView",
            Namespace = "Contoso.Sales.Views",
            RootEntityTypeFullName = "Contoso.Sales.Entities.Order",
            GroupByProperties =
            [
                new GroupByModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    EntityType = "Contoso.Sales.Entities.Order",
                    EntityProperty = "Status"
                },
                new GroupByModel
                {
                    PropertyName = "Year",
                    PropertyType = "int",
                    EntityType = "Contoso.Sales.Entities.Order",
                    EntityProperty = "CreatedAt.Year"
                }
            ],
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "TotalRevenue",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Sum,
                    EntityType = "Contoso.Sales.Entities.Order",
                    Expression = "e.TotalAmount"
                },
                new AggregatePropertyModel
                {
                    PropertyName = "OrderCount",
                    PropertyType = "int",
                    Kind = AggregateKind.Count,
                    EntityType = "Contoso.Sales.Entities.Order",
                    Expression = "e"
                },
                new AggregatePropertyModel
                {
                    PropertyName = "AverageOrderValue",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Average,
                    EntityType = "Contoso.Sales.Entities.Order",
                    Expression = "e.TotalAmount"
                }
            ]
        };

        // Act
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();

        // Assert
        var source = artifact.Text;

        source.Should().Contain("namespace Contoso.Sales.Views;");
        source.Should().Contain("partial class SalesReportView");
        source.Should().Contain("GroupBy");
        source.Should().Contain("Status");
        source.Should().Contain("Sum");
        source.Should().Contain("Count");
        source.Should().Contain("Average");
    }

    private static QueryViewModel CreateBasicModel()
    {
        return new QueryViewModel
        {
            TypeName = "OrderSummaryView",
            Namespace = "MyApp.Sales.Views",
            RootEntityTypeFullName = "MyApp.Sales.Entities.Order",
            GroupByProperties =
            [
                new GroupByModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityProperty = "Status"
                }
            ],
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "TotalAmount",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Sum,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e.Amount"
                }
            ]
        };
    }
}
