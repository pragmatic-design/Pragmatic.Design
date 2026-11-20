using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     On EF Core a trait's child gets the repository every entity has (#888), and its trait metadata names it for the host to register.
/// </summary>
/// <remarks>
///     The shape only: that the host registers it and a row is read and saved through it is the Showcase's
///     <c>AttachmentEndpointTests</c>, through <c>Infrastructure/AttachmentRows.cs</c>, on EF Core and on both engines.
/// </remarks>
public class ATraitsChildHasARepositoryOnEfCoreTests
{
    private const string Source = """
        using Pragmatic.Attachments;
        using Pragmatic.Tags;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Catalog.Products
        {
            public sealed class CatalogBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
            [Resource("products")]
            [HasAttachments]
            [HasTags]
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

    [Fact]
    public void TheChild_HasARepositoryKeyedOnTheBoundary_ReadingByTheKeyItsModelMaps()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var repository = sources.Should()
            .ContainKey("Catalog.Products.ProductAttachment.Repository.g.cs", "a trait's child has a repository on EF Core, as on the engines")
            .WhoseValue;
        repository.Should().Contain("global::Pragmatic.Persistence.Repository.IRepository<global::Catalog.Products.ProductAttachment>");
        repository.Should().Contain("[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::Catalog.Products.CatalogBoundary))]");
        // The child's configuration maps Id and ignores PersistenceId: a read on PersistenceId does not translate.
        repository.Should().Contain("e => e.Id.Equals(id)");
        repository.Should().NotContain("e.PersistenceId");
        // No columns in a model built from the trait, so no bulk writes over them.
        repository.Should().NotContain("IBulkRepository");

        sources.Should().ContainKey("Catalog.Products.CatalogTag.Repository.g.cs", "the shared tag is an IEntity keyed on its Id");
    }

    [Fact]
    public void TheTraitMetadata_NamesEachRepository_AndNoneForTheTagLink()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);

        var metadata = string.Join("\n", sources.Values.Where(s => s.Contains("PragmaticMetadata((MetadataCategory)19")));
        metadata.Should().Contain("\"repositoryType\":\"Catalog.Products.ProductAttachment.Repository\"");
        metadata.Should().Contain("\"repositoryType\":\"Catalog.Products.CatalogTag.Repository\"");
        // Keyed on its two foreign keys, and no IEntity on EF Core.
        metadata.Should().NotContain("\"repositoryType\":\"Catalog.Products.ProductTagLink.Repository\"");
        sources.Should().NotContainKey("Catalog.Products.ProductTagLink.Repository.g.cs");
    }
}
