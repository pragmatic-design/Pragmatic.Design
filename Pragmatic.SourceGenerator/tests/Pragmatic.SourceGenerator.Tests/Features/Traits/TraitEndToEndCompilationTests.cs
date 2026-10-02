using System;
using System.IO;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// END-TO-END compilation tests for the trait source generators.
/// Unlike <see cref="TraitSnapshotTests"/> (which exercise a single template on a
/// hand-built model), these run the FULL <see cref="PragmaticSourceGenerator"/> over a
/// realistic entity decorated with a trait attribute, then assert the resulting
/// compilation (user source + ALL generated trait code) has no compiler errors.
///
/// This verifies the generated trait code actually compiles end to end.
/// </summary>
public class TraitEndToEndCompilationTests
{
    // Names of generated files that belong specifically to the trait pipeline.
    // An [Entity] also drives the Persistence/Query/DI-registration pipeline whose output
    // (Repository, _Infra.*.Registration) needs the full host reference closure; those are
    // outside this verification's scope. We assert ONLY on the trait-owned artifacts.
    private static Func<string, bool> TraitFilesNamed(params string[] markers) => path =>
    {
        var f = Path.GetFileName(path);
        // Per-type trait artifacts are named after the generated trait types, all of which
        // contain the trait marker (e.g. ProductTag.Entity.g.cs, AddTicketNoteAction.*.g.cs,
        // Product.TagNavigation.g.cs, TicketNoteDto.Dto.g.cs). Exclude the parent entity's own
        // persistence/infra output (Repository, Specs, _Infra.*, EntityConfig of the parent).
        if (!f.EndsWith(".g.cs", StringComparison.Ordinal)) return false;
        if (f.StartsWith("_Infra.", StringComparison.Ordinal)) return false;
        if (f.Contains(".Repository.", StringComparison.Ordinal)) return false;
        if (f.Contains(".Specs.", StringComparison.Ordinal)) return false;
        foreach (var marker in markers)
            if (f.Contains(marker, StringComparison.Ordinal)) return true;
        return false;
    };

    [Fact]
    public void HasTags_OnEntity_GeneratesCompilingCode()
    {
        // A realistic boundary entity decorated with [HasTags].
        //   [Entity]  → makes it a Pragmatic entity with Guid id
        //   [BelongsTo<CatalogBoundary>] → boundary resolution (trait transform reads this)
        //   [Resource("products")] → enables endpoint generation
        //   [PragmaticDbContext("Catalog")] → enables the EF Core trait pipeline
        var source = """
            using Pragmatic.Tags;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.EFCore;

            namespace Catalog.Products
            {
                public sealed class CatalogBoundary { }

                [Entity]
                [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
                [Resource("products")]
                [HasTags(MaxPerEntity = 10, AllowCustom = true)]
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

        var (traitErrors, _) = TraitCompilationHarness.CompileAndSplitErrors(source, TraitFilesNamed("Tag"));

        traitErrors.Should().BeEmpty(
            "generated [HasTags] trait code (Tag entity, junction, navigation, actions, "
            + "permissions, endpoints) must compile. Errors:"
            + Environment.NewLine + TraitCompilationHarness.FormatErrors(traitErrors));
    }

    /// <summary>
    ///     The documented read side is generated. Assert it from real source: the DTO, the list query
    ///     and the GET endpoint must all exist — and compile.
    /// </summary>
    [Fact]
    public void HasTags_OnEntity_GeneratesReadSide()
    {
        var source = """
            using Pragmatic.Tags;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.EFCore;

            namespace Catalog.Products
            {
                public sealed class CatalogBoundary { }

                [Entity]
                [Pragmatic.Persistence.Entity.BelongsTo<CatalogBoundary>]
                [Resource("products")]
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

        var (sources, _) = TraitCompilationHarness.Generate(source);

        var dto = sources.Should()
            .ContainKey("Catalog.Products.ProductTagDto.Dto.g.cs", "the [HasTags] read side needs a DTO")
            .WhoseValue;
        dto.Should().Contain("public sealed class ProductTagDto");
        dto.Should().Contain("Value = e.Tag!.Value,");

        var query = sources.Should()
            .ContainKey("Catalog.Products.ListProductTagsQuery.QueryClass.g.cs")
            .WhoseValue;
        query.Should().Contain("[Query<ProductTagLink, ProductTagDto>]");
        query.Should().Contain("public required Guid ProductId { get; init; }");
        query.Should().Contain("init => _pageSize = value is < 1 or > 200 ? 20 : value");

        var endpoint = sources.Should()
            .ContainKey("Catalog.Products.ListProductTagsQuery.Endpoint.g.cs",
                "the GET /tags endpoint is generated from the injected query endpoint model")
            .WhoseValue;
        endpoint.Should().Contain("MapGet(\"/api/catalog/products/{productId}/tags\"");
        endpoint.Should().Contain("catalog.product.tags.read");

        // The Apply/Projection half of the query lives in a separate file emitted by the Query feature.
        sources.Should().ContainKey("Catalog.Products.ListProductTagsQuery.Query.g.cs");

        var (traitErrors, _) = TraitCompilationHarness.CompileAndSplitErrors(source, TraitFilesNamed("Tag"));
        traitErrors.Should().BeEmpty(
            "the generated [HasTags] read side must compile. Errors:"
            + Environment.NewLine + TraitCompilationHarness.FormatErrors(traitErrors));
    }

    [Fact]
    public void HasNotes_OnEntity_GeneratesCompilingCode()
    {
        var source = """
            using Pragmatic.Notes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.EFCore;

            namespace Support.Tickets
            {
                public sealed class SupportBoundary { }

                [Entity]
                [Pragmatic.Persistence.Entity.BelongsTo<SupportBoundary>]
                [Resource("tickets")]
                [HasNotes(MaxLength = 2000, AllowEditing = true)]
                public partial class Ticket : IEntity
                {
                    public System.Guid Id { get; set; }
                    public System.Guid PersistenceId { get => Id; set => Id = value; }
                    public string Subject { get; set; } = string.Empty;
                }

                [PragmaticDbContext("Support")]
                public partial class SupportDbContext { }
            }
            """;

        var (traitErrors, _) = TraitCompilationHarness.CompileAndSplitErrors(source, TraitFilesNamed("Note"));

        traitErrors.Should().BeEmpty(
            "generated [HasNotes] trait code (Note entity, navigation, actions, permissions, "
            + "DTO, list query, endpoints) must compile. Errors:"
            + Environment.NewLine + TraitCompilationHarness.FormatErrors(traitErrors));
    }

    [Fact]
    public void HasComments_OnEntity_GeneratesCompilingCode()
    {
        // EditWindowMinutes as an attribute literal is the whole point: as int? it would not be a
        // legal attribute-argument type, so this source would not compile (CS0655) and the
        // generator branch reading it would be unreachable.
        var source = """
            using Pragmatic.Comments;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.EFCore;

            namespace Blog.Articles
            {
                public sealed class BlogBoundary { }

                [Entity]
                [Pragmatic.Persistence.Entity.BelongsTo<BlogBoundary>]
                [Resource("articles")]
                [HasComments(MaxLength = 1000, EditWindowMinutes = 30)]
                public partial class Article : IEntity
                {
                    public System.Guid Id { get; set; }
                    public System.Guid PersistenceId { get => Id; set => Id = value; }
                    public string Title { get; set; } = string.Empty;
                }

                [PragmaticDbContext("Blog")]
                public partial class BlogDbContext { }
            }
            """;

        var (traitErrors, _) = TraitCompilationHarness.CompileAndSplitErrors(source, TraitFilesNamed("Comment"));

        traitErrors.Should().BeEmpty(
            "generated [HasComments] trait code (Comment entity, navigation, actions, permissions, "
            + "DTO, list query, endpoints) must compile. Errors:"
            + Environment.NewLine + TraitCompilationHarness.FormatErrors(traitErrors));
    }

    [Fact]
    public void HasAttachments_OnEntity_GeneratesCompilingCode()
    {
        var source = """
            using Pragmatic.Attachments;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.EFCore;

            namespace Billing.Invoices
            {
                public sealed class BillingBoundary { }

                [Entity]
                [Pragmatic.Persistence.Entity.BelongsTo<BillingBoundary>]
                [Resource("invoices")]
                [HasAttachments(MaxPerEntity = 5, AllowedExtensions = ".pdf,.png")]
                public partial class Invoice : IEntity
                {
                    public System.Guid Id { get; set; }
                    public System.Guid PersistenceId { get => Id; set => Id = value; }
                    public string Number { get; set; } = string.Empty;
                }

                [PragmaticDbContext("Billing")]
                public partial class BillingDbContext { }
            }
            """;

        var (traitErrors, _) = TraitCompilationHarness.CompileAndSplitErrors(source, TraitFilesNamed("Attachment"));

        traitErrors.Should().BeEmpty(
            "generated [HasAttachments] trait code (Attachment entity, EF config, navigation, "
            + "upload/get/delete actions, permissions, DTO, list query, endpoints) must compile. Errors:"
            + Environment.NewLine + TraitCompilationHarness.FormatErrors(traitErrors));
    }
}
