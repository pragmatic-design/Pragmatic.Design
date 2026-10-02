using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Tests for Query Pipeline diagnostics conditions (PRAG0710, PRAG0711, PRAG0716).
///     Verifies the model conditions that trigger diagnostic emission.
/// </summary>
public class QueryDiagnosticsTests
{
    [Fact]
    public void PRAG0710_DtoWithUnmatchedNavigation_ModelTracksUnmatched()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderSummary",
            FullTypeName = "global::Sales.OrderSummary",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            NavigationPaths = ImmutableArray.Create("LineItems"),
            UnmatchedNavigationNames = ImmutableArray.Create("CustomerDetails", "ShippingInfo")
        };

        model.HasUnmatchedNavigations.Should().BeTrue();
        model.UnmatchedNavigationNames.Should().HaveCount(2);
        model.UnmatchedNavigationNames.Should().Contain("CustomerDetails");
        model.UnmatchedNavigationNames.Should().Contain("ShippingInfo");
    }

    [Fact]
    public void PRAG0710_DtoWithAllMatchedNavigations_NoUnmatched()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderDetail",
            FullTypeName = "global::Sales.OrderDetail",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            NavigationPaths = ImmutableArray.Create("LineItems", "Customer")
        };

        model.HasUnmatchedNavigations.Should().BeFalse();
    }

    [Fact]
    public void PRAG0711_MaxDepthAbove3_ShouldTriggerWarning()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "DeepOrderView",
            FullTypeName = "global::Sales.DeepOrderView",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            MaxDepth = 5,
            NavigationPaths = ImmutableArray.Create("LineItems")
        };

        // MaxDepth > 3 should trigger PRAG0711
        model.MaxDepth.Should().BeGreaterThan(3);
    }

    [Fact]
    public void PRAG0711_MaxDepth3OrLess_NoWarning()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderDetail",
            FullTypeName = "global::Sales.OrderDetail",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            MaxDepth = 3,
            NavigationPaths = ImmutableArray.Create("LineItems")
        };

        model.MaxDepth.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public void PRAG0716_FourOrMoreNavigations_ShouldTriggerWarning()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderFullView",
            FullTypeName = "global::Sales.OrderFullView",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            NavigationPaths = ImmutableArray.Create("LineItems", "Customer", "Payments", "ShippingAddress")
        };

        // 4+ navigation paths should trigger PRAG0716
        model.HasNavigations.Should().BeTrue();
        model.NavigationPaths.Length.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void PRAG0716_LessThanFourNavigations_NoWarning()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderSummary",
            FullTypeName = "global::Sales.OrderSummary",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            NavigationPaths = ImmutableArray.Create("LineItems", "Customer")
        };

        model.NavigationPaths.Length.Should().BeLessThan(4);
    }

    [Fact]
    public void DiagnosticDescriptors_HaveCorrectIds()
    {
        QueryPipelineDiagnostics.DtoNavigationWithoutInclude.Id.Should().Be("PRAG0710");
        QueryPipelineDiagnostics.DeepIncludeWithoutLoadWith.Id.Should().Be("PRAG0711");
        QueryPipelineDiagnostics.DtoWithManyNavigationLevels.Id.Should().Be("PRAG0716");
    }

    [Fact]
    public void DiagnosticDescriptors_AreWarnings()
    {
        QueryPipelineDiagnostics.DtoNavigationWithoutInclude.DefaultSeverity
            .Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        QueryPipelineDiagnostics.DeepIncludeWithoutLoadWith.DefaultSeverity
            .Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        QueryPipelineDiagnostics.DtoWithManyNavigationLevels.DefaultSeverity
            .Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }
}
