using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Collections of primitives (List&lt;string&gt;, string[], …) must persist as EF Core
///     primitive collections (JSON column) via <c>builder.Property(...)</c> instead of being skipped
///     as navigations. Any primitive collection qualifies, not only <c>AccessScopes</c> on
///     [HasAccessScopes].
/// </summary>
public class PrimitiveCollectionConfigTests
{
    [Fact]
    public void PrimitiveCollection_EmitsPropertyWithColumnName()
    {
        var model = BuildModel("Tags");

        var source = Render(model);

        source.Should().Contain("builder.Property(e => e.Tags).HasColumnName(\"Tags\");");
    }

    [Fact]
    public void MultiplePrimitiveCollections_AllEmitted()
    {
        var model = BuildModel("Tags", "Codes");

        var source = Render(model);

        source.Should().Contain("builder.Property(e => e.Tags).HasColumnName(\"Tags\");");
        source.Should().Contain("builder.Property(e => e.Codes).HasColumnName(\"Codes\");");
    }

    [Fact]
    public void NoPrimitiveCollections_EmitsNothingExtra()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Order",
            FullTypeName = "Sales.Order",
            Namespace = "Sales",
            IdType = "Guid",
            IsValid = true,
        };

        var source = Render(model);

        source.Should().NotContain("HasColumnName(\"Tags\")");
    }

    private static string Render(EntityMetadataModel model)
        => new EntityConfigurationTemplate(model).RenderOutput().Text;

    private static EntityMetadataModel BuildModel(params string[] collectionNames)
    {
        return new EntityMetadataModel
        {
            TypeName = "Product",
            FullTypeName = "Catalog.Product",
            Namespace = "Catalog",
            IdType = "Guid",
            IsValid = true,
            PrimitiveCollectionProperties = collectionNames
                .Select(n => new PrimitiveCollectionMetadataModel { Name = n })
                .ToEquatableArray(),
        };
    }
}
