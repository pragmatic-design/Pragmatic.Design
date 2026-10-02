using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     A trait on an entity whose author wrote <c>[Entity]</c> and nothing else. <c>IEntity</c> is added
///     by the generator's own partial, which the generator does not see while it runs, so a trait
///     transform that resolved the key type by looking for <c>IEntity</c> would find nothing, return no
///     model, and the trait would vanish — while PRAG2600 stays quiet, because <c>[Entity]</c> is there.
/// </summary>
/// <remarks>
///     Every other trait test writes <c>: IEntity</c> by hand, which is why this case needs its own.
/// </remarks>
public class ATraitOnAnEntityWithoutIEntityTests
{
    private static string Source(string trait, bool writesIEntity) => $$"""
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Catalog.Products
        {
            public sealed class CatalogBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
            [Resource("products")]
            [{{trait}}]
            public partial class Product{{(writesIEntity ? " : IEntity" : "")}}
            {
                public string Name { get; set; } = string.Empty;
                {{(writesIEntity ? "public System.Guid PersistenceId { get; set; }" : "")}}
            }

            [PragmaticDbContext("Catalog")]
            public partial class CatalogDbContext { }
        }
        """;

    [Theory]
    [InlineData("Pragmatic.Comments.HasComments", "ProductComment")]
    [InlineData("Pragmatic.Tags.HasTags", "ProductTag")]
    [InlineData("Pragmatic.Notes.HasNotes", "ProductNote")]
    [InlineData("Pragmatic.Attachments.HasAttachments", "ProductAttachment")]
    public void Generate_TraitOnEntityWithoutIEntity_GeneratesTheTrait(string trait, string generatedType)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source(trait, writesIEntity: false));

        sources.Keys.Should().Contain(k => k.Contains(generatedType),
            $"[Entity] makes Product an entity; the {trait} trait must be generated without a hand-written IEntity");
    }

    /// <summary>The control: the same entity with <c>: IEntity</c> written by hand, which always worked.</summary>
    [Theory]
    [InlineData("Pragmatic.Comments.HasComments", "ProductComment")]
    [InlineData("Pragmatic.Tags.HasTags", "ProductTag")]
    [InlineData("Pragmatic.Notes.HasNotes", "ProductNote")]
    [InlineData("Pragmatic.Attachments.HasAttachments", "ProductAttachment")]
    public void Generate_TraitOnEntityWithIEntity_GeneratesTheTrait(string trait, string generatedType)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source(trait, writesIEntity: true));

        sources.Keys.Should().Contain(k => k.Contains(generatedType));
    }
}
