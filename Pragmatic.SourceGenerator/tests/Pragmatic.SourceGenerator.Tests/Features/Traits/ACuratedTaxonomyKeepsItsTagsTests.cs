using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     With <c>[HasTags(AllowCustom = false)]</c>, removing a tag's last link leaves the tag in place.
/// </summary>
/// <remarks>
///     A remove that deletes a tag once nothing links it keeps an open vocabulary free of typos, and
///     erodes a curated one: the application seeds its tags, no endpoint creates them, and the first
///     time a tag's last use is removed it is gone for good.
/// </remarks>
public class ACuratedTaxonomyKeepsItsTagsTests
{
    private const string RemoveAction = "Catalog.Products.RemoveProductTagAction.Action.g.cs";

    [Fact]
    public void ACuratedTaxonomy_DoesNotDeleteAnUnusedTag()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source(allowCustom: false));

        sources.Should().ContainKey(RemoveAction)
            .WhoseValue.Should().NotContain("ExecuteDeleteAsync");

        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Source(allowCustom: false), path => path.EndsWith(RemoveAction, System.StringComparison.Ordinal));
        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>The control: an open vocabulary still cleans up a tag nobody uses.</summary>
    [Fact]
    public void AnOpenVocabulary_DeletesAnUnusedTag()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source(allowCustom: true));

        sources.Should().ContainKey(RemoveAction)
            .WhoseValue.Should().Contain("ExecuteDeleteAsync");
    }

    private static string Source(bool allowCustom) => $$"""
        using Pragmatic.Tags;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Catalog.Products
        {
            public sealed class CatalogBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
            [Resource("products")]
            [HasTags(AllowCustom = {{(allowCustom ? "true" : "false")}})]
            public partial class Product : IEntity
            {
                public System.Guid Id { get; set; }
                public System.Guid PersistenceId { get => Id; set => Id = value; }
                public string Name { get; set; } = string.Empty;
            }

            [PragmaticDbContext("Catalog")]
            public partial class CatalogDbContext { }
        }
        """;
}
