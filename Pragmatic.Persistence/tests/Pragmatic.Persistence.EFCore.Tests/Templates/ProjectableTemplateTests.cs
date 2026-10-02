using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     Unit tests for <see cref="ProjectableTemplate"/>.
///     Verifies the generated nested Expr class with Expression projections.
/// </summary>
public class ProjectableTemplateTests
{
    [Fact]
    public void SingleProperty_GeneratesExprClass()
    {
        var model = CreateBasicModel();

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("public partial class Order");
        source.Should().Contain("public static class Expr");
        source.Should().Contain("Expression<Func<global::TestApp.Order, decimal>>");
        source.Should().Contain("Total => e => e.SubTotal + e.Tax");
    }

    [Fact]
    public void MultipleProperties_GeneratesAllProjections()
    {
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new ProjectablePropertyModel
                {
                    EntityNamespace = "TestApp",
                    EntityTypeName = "Order",
                    EntityAccessibility = "public",
                    PropertyName = "Total",
                    ReturnType = "decimal",
                    ExpressionBody = "e.SubTotal + e.Tax",
                    PortableBody = "__pragmatic_source__.SubTotal + __pragmatic_source__.Tax"
                },
                new ProjectablePropertyModel
                {
                    EntityNamespace = "TestApp",
                    EntityTypeName = "Order",
                    EntityAccessibility = "public",
                    PropertyName = "FullName",
                    ReturnType = "string",
                    ExpressionBody = "$\"{e.FirstName} {e.LastName}\"",
                    PortableBody = "$\"{__pragmatic_source__.FirstName} {__pragmatic_source__.LastName}\""
                })
        };

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("Total => e => e.SubTotal + e.Tax");
        source.Should().Contain("FullName => e => $\"{e.FirstName} {e.LastName}\"");
    }

    /// <summary>
    ///     The body is published as text too, escaped as a literal, for a projection compiled in
    ///     another assembly — which has no syntax to read it from.
    /// </summary>
    [Fact]
    public void EachProjection_PublishesItsBody_ForOtherAssemblies()
    {
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new ProjectablePropertyModel
                {
                    EntityNamespace = "TestApp",
                    EntityTypeName = "Order",
                    EntityAccessibility = "public",
                    PropertyName = "FullName",
                    ReturnType = "string",
                    ExpressionBody = "e.FirstName + \" \" + e.LastName",
                    PortableBody = "__pragmatic_source__.FirstName + \" \" + __pragmatic_source__.LastName"
                })
        };

        var source = new ProjectableTemplate(model).RenderOutput().Text;

        source.Should().Contain(
            "[global::Pragmatic.Persistence.Query.Attributes.ProjectableBody("
            + "\"__pragmatic_source__.FirstName + \\\" \\\" + __pragmatic_source__.LastName\")]");
    }

    [Fact]
    public void GeneratesCorrectHintName()
    {
        var model = CreateBasicModel();

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Order");
        artifact.HintName.Should().Contain("Projectable");
    }

    [Fact]
    public void InternalEntity_GeneratesInternalPartialClass()
    {
        var model = CreateBasicModel() with
        {
            Accessibility = "internal"
        };

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("internal partial class Order");
    }

    [Fact]
    public void GeneratesNamespace()
    {
        var model = CreateBasicModel();

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("namespace TestApp;");
    }

    [Fact]
    public void GeneratesRequiredUsings()
    {
        var model = CreateBasicModel();

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("using System;");
        source.Should().Contain("using System.Linq.Expressions;");
    }

    [Fact]
    public void GeneratesXmlDocs()
    {
        var model = CreateBasicModel();

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("/// <summary>");
        source.Should().Contain("Expression projection for");
        source.Should().Contain("Order.Total");
    }

    [Fact]
    public void GeneratesHeader()
    {
        var model = CreateBasicModel();

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("// Pragmatic.SourceGenerator/Persistence");
        source.Should().Contain("// Source: Order from TestApp");
        source.Should().Contain("// Trigger: [Projectable] on Order");
    }

    [Fact]
    public void InvalidModel_ReturnsEmptySource()
    {
        var model = new ProjectableModel
        {
            TypeName = "",
            Accessibility = "public",
            Properties = ImmutableArray<ProjectablePropertyModel>.Empty
        };

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void FullyQualifiedReturnType_GeneratesCorrectExpression()
    {
        var model = CreateBasicModel() with
        {
            Properties = ImmutableArray.Create(
                new ProjectablePropertyModel
                {
                    EntityNamespace = "TestApp",
                    EntityTypeName = "Order",
                    EntityAccessibility = "public",
                    PropertyName = "Customer",
                    ReturnType = "global::TestApp.Customer",
                    ExpressionBody = "e.Nav",
                    PortableBody = "__pragmatic_source__.Nav"
                })
        };

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("Expression<Func<global::TestApp.Order, global::TestApp.Customer>>");
    }

    [Fact]
    public void DeepNamespace_GeneratesCorrectFullTypeName()
    {
        var model = CreateBasicModel() with
        {
            Namespace = "MyApp.Sales.Entities"
        };

        var template = new ProjectableTemplate(model);
        var artifact = template.RenderOutput();

        var source = artifact.Text;
        source.Should().Contain("namespace MyApp.Sales.Entities;");
        source.Should().Contain("global::MyApp.Sales.Entities.Order");
    }

    private static ProjectableModel CreateBasicModel()
    {
        return new ProjectableModel
        {
            Namespace = "TestApp",
            TypeName = "Order",
            Accessibility = "public",
            Properties = ImmutableArray.Create(
                new ProjectablePropertyModel
                {
                    EntityNamespace = "TestApp",
                    EntityTypeName = "Order",
                    EntityAccessibility = "public",
                    PropertyName = "Total",
                    ReturnType = "decimal",
                    ExpressionBody = "e.SubTotal + e.Tax",
                    PortableBody = "__pragmatic_source__.SubTotal + __pragmatic_source__.Tax"
                })
        };
    }
}
