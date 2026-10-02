using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

public class ComputedFilterTemplateTests
{
    [Fact]
    public void BooleanProperty_GeneratesSpecification()
    {
        var model = CreateModel("IsOverdue", "e.DueDate < global::System.DateTimeOffset.UtcNow");
        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("IsOverdueSpec");
        source.Should().Contain("Spec<");
        source.Should().Contain(".Where(e =>");
    }

    [Fact]
    public void BooleanProperty_GeneratesWhereExtension()
    {
        var model = CreateModel("IsOverdue", "e.DueDate < global::System.DateTimeOffset.UtcNow");
        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("WhereOverdue");
        source.Should().Contain("this global::System.Linq.IQueryable<");
    }

    [Fact]
    public void WhereMethodName_StripsIsPrefix()
    {
        var model = CreateModel("IsActive", "e.Status == 1");
        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // "IsActive" → "WhereActive" (strips "Is" prefix)
        source.Should().Contain("WhereActive");
        source.Should().NotContain("WhereIsActive");
    }

    [Fact]
    public void WhereMethodName_HasPrefix_KeepsIt()
    {
        var model = CreateModel("HasBalance", "e.Balance > 0");
        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // "HasBalance" → "WhereHasBalance" (no stripping)
        source.Should().Contain("WhereHasBalance");
    }

    [Fact]
    public void ClassName_UsesNamingHelper()
    {
        var model = CreateModel("IsOverdue", "e.DueDate < now");
        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("InvoiceComputedFilters");
    }

    [Fact]
    public void GeneratesCorrectHintName()
    {
        var model = CreateModel("IsOverdue", "e.DueDate < now");
        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Invoice");
        artifact.HintName.Should().Contain("ComputedFilter");
    }

    [Fact]
    public void MultipleProperties_GeneratesAll()
    {
        var model = new ComputedFilterModel
        {
            Namespace = "MyApp.Billing",
            TypeName = "Invoice",
            Accessibility = "public",
            Properties = ImmutableArray.Create(
                new ComputedFilterPropertyModel { PropertyName = "IsOverdue", ExpressionBody = "e.DueDate < now" },
                new ComputedFilterPropertyModel { PropertyName = "IsPaid", ExpressionBody = "e.Status == 1" }
            )
        };

        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("IsOverdueSpec");
        source.Should().Contain("IsPaidSpec");
        source.Should().Contain("WhereOverdue");
        source.Should().Contain("WherePaid");
    }

    [Fact]
    public void InvalidModel_ReturnsNoSource()
    {
        var model = new ComputedFilterModel
        {
            Namespace = "",
            TypeName = "",
            Accessibility = "public",
            Properties = ImmutableArray<ComputedFilterPropertyModel>.Empty
        };

        var template = new ComputedFilterTemplate(model);
        var artifact = template.RenderOutput();

        // Invalid model produces empty or null source
        var hasContent = artifact.Text.Contains("class") == true;
        hasContent.Should().BeFalse();
    }

    private static ComputedFilterModel CreateModel(string propertyName, string expressionBody)
    {
        return new ComputedFilterModel
        {
            Namespace = "MyApp.Billing",
            TypeName = "Invoice",
            Accessibility = "public",
            Properties = ImmutableArray.Create(
                new ComputedFilterPropertyModel
                {
                    PropertyName = propertyName,
                    ExpressionBody = expressionBody
                }
            )
        };
    }
}
