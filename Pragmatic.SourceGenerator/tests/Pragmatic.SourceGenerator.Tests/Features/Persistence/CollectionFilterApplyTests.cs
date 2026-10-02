using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     BE-03 (apply layer): a collection-of-scalars filter property (List&lt;string&gt; Type) applies
///     <c>query.Type.Contains(e.Type)</c> (the In operator) and is skipped when the collection is null
///     OR empty — an empty collection must not filter out every row.
/// </summary>
public class CollectionFilterApplyTests
{
    [Fact]
    public void CollectionFilter_AppliesInOperator()
    {
        var source = Render(CollectionFilterProp());

        source.Should().Contain("this.Type.Contains(e.Type)");
    }

    [Fact]
    public void CollectionFilter_SkipsWhenNullOrEmpty()
    {
        var source = Render(CollectionFilterProp());

        source.Should().Contain("this.Type != null && System.Linq.Enumerable.Any(this.Type)");
    }

    private static QueryPropertyModel CollectionFilterProp() => new()
    {
        PropertyName = "Type",
        PropertyType = "System.Collections.Generic.List<string>",
        IsFilter = true,
        IsCollection = true,
        Operator = FilterOperatorKind.In,
    };

    private static string Render(QueryPropertyModel prop)
    {
        var model = new QueryModel
        {
            Namespace = "Catalog.Queries",
            TypeName = "ProductQuery",
            Accessibility = "public",
            TypeKind = "record",
            IsRecord = true,
            EntityTypeFullName = "Catalog.Product",
            EntityTypeName = "Product",
            ResultTypeFullName = "Catalog.Product",
            ResultTypeName = "Product",
            Properties = new[] { prop }.ToEquatableArray(),
        };

        return new QueryApplyTemplate(model).RenderOutput().Text;
    }
}
