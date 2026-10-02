using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Reading a child without reading its parent is reported, not left to fail at the first query.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>[ReadAccess&lt;TChild&gt;]</c> brings the owner's configuration for the child, and that
///         configuration declares the child's reference navigation to its parent. The parent is a type
///         the reader neither owns nor reads, so it goes into <c>modelBuilder.Ignore&lt;T&gt;()</c> —
///         and the declared navigation has nothing left to point at.
///     </para>
///     <para>
///         The model still builds, which is what makes this quiet: the <c>DbSet</c> is there, the
///         configuration is applied, and the navigation is a property that exists on the CLR type.
///         What fails is the first query that names it, at run time, in an application that compiled
///         clean.
///     </para>
///     <para>
///         Not answered by pulling the parent in: <c>[ReadAccess]</c> would then reach as far as the
///         object graph does, which is unsafe. The reader is told, and decides.
///     </para>
///     <para>
///         Three compilations, because one cannot see this at all: the owner declares the entities,
///         the reader declares the read, and only a compilation holding both metadata documents can
///         compare them.
///     </para>
/// </remarks>
public class AReadChildWhoseParentIsUnreadTests
{
    /// <summary>The owner: a parent, and a child that points back at it.</summary>
    private const string OwnerModule = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        public partial class KnowledgeItem : IEntity
        {
            public string Term { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<CatalogBoundary>]
        [Relation.ManyToOne<KnowledgeItem>.WithNavigation("Item")]
        public partial class TermMention : IEntity
        {
            public string Snippet { get; private set; } = "";
        }
        """;

    /// <summary>An owner whose read child carries a many-to-many with an explicit join entity.</summary>
    private const string OwnerWithAJoin = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        public partial class Amenity : IEntity
        {
            public string Label { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<CatalogBoundary>]
        public partial class RoomAmenity : IEntity
        {
            public bool IsIncluded { get; private set; }
        }

        [Entity]
        [BelongsTo<CatalogBoundary>]
        [Relation.ManyToMany<Amenity, RoomAmenity>.WithNavigation("Amenities")]
        public partial class Room : IEntity
        {
            public string Number { get; private set; } = "";
        }
        """;

    /// <summary>A reader that takes the room and both ends of the relation, but not the join.</summary>
    private const string ReadsTheRoomAndTheAmenity = """
        using Catalog;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Work;

        [Boundary]
        [ReadAccess<Room>]
        [ReadAccess<Amenity>]
        public partial class WorkBoundary;

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class Story : IEntity
        {
            public string Title { get; private set; } = "";
        }
        """;

    /// <summary>An owner whose entity relates to itself through a join entity.</summary>
    private const string OwnerWithASelfRelation = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        public partial class TermRelation : IEntity
        {
            public string Kind { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<CatalogBoundary>]
        [Relation.ManyToMany<Term, TermRelation>.WithNavigation("RelatedTo")]
        public partial class Term : IEntity
        {
            public string Label { get; private set; } = "";
        }
        """;

    /// <summary>A reader that takes the self-related entity and nothing else.</summary>
    private const string ReadsTheSelfRelated = """
        using Catalog;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Work;

        [Boundary]
        [ReadAccess<Term>]
        public partial class WorkBoundary;

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class Story : IEntity
        {
            public string Title { get; private set; } = "";
        }
        """;

    /// <summary>The reader that takes the child and leaves the parent behind.</summary>
    private const string ReadsTheChildOnly = """
        using Catalog;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Work;

        [Boundary]
        [ReadAccess<TermMention>]
        public partial class WorkBoundary;

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class Story : IEntity
        {
            public string Title { get; private set; } = "";
        }
        """;

    /// <summary>The control: the same reader, taking both.</summary>
    private const string ReadsBoth = """
        using Catalog;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Work;

        [Boundary]
        [ReadAccess<TermMention>]
        [ReadAccess<KnowledgeItem>]
        public partial class WorkBoundary;

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class Story : IEntity
        {
            public string Title { get; private set; } = "";
        }
        """;

    private const string Host = """
        namespace AppHost;

        public static class Program
        {
            public static void Main() { }
        }
        """;

    /// <summary>The pair is reported, naming both types.</summary>
    [Fact]
    public void AChildReadWithoutItsParent_IsReported()
    {
        var reported = HostDiagnostics(ReadsTheChildOnly);

        reported.Should().Contain(d => d.Id == "PRAG0639",
            "the navigation the owner's configuration declares has nothing to point at");

        var message = reported.First(d => d.Id == "PRAG0639").GetMessage();
        message.Should().Contain("TermMention", "the reader has to know which read to look at");
        message.Should().Contain("KnowledgeItem", "and which type is missing from it");
    }

    /// <summary>
    ///     It is a <b>Warning</b>: the reader has to see it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It shipped as Info, because following its own advice on the Showcase broke the build a
    ///     second way — reading <c>Amenity</c> drags in a many-to-many whose join entity is still
    ///     ignored. Info is the severity this repository documents as off by default for
    ///     <c>PRAG0413</c>, so the four latent instances were reported to nobody. The cascade is the
    ///     work, not a reason to whisper: with <c>--warnaserror</c> a Warning is what makes an
    ///     incomplete read impossible to ship.
    /// </remarks>
    [Fact]
    public void TheReport_IsAWarning()
    {
        var reported = HostDiagnostics(ReadsTheChildOnly).First(d => d.Id == "PRAG0639");

        reported.Severity.Should().Be(DiagnosticSeverity.Warning,
            "a query that cannot translate is a defect the author must see, not a note");
    }

    /// <summary>
    ///     The control: reading both is silent.
    /// </summary>
    /// <remarks>
    ///     Without it, "the pair is reported" is satisfied by a diagnostic on every
    ///     <c>[ReadAccess]</c> that names an entity with any navigation at all — which is most of them,
    ///     and the feature would become unusable rather than safe.
    /// </remarks>
    [Fact]
    public void AChildReadWithItsParent_IsSilent()
    {
        HostDiagnostics(ReadsBoth).Should().NotContain(d => d.Id == "PRAG0639");
    }

    /// <summary>
    ///     The join entity of a many-to-many is named too, and it is the one that stops the model.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without the join named, following this diagnostic to the letter leaves the application
    ///     broken: reading a type at both ends of a many-to-many still omits the join, and EF then
    ///     refuses to build the model at all — "the skip navigation 'X.Ys' doesn't have a foreign key
    ///     associated with it". Every request answers 500, not only the one that reads through the
    ///     navigation, which is worse than the defect this reports.
    /// </remarks>
    [Fact]
    public void AManyToManyWhoseJoinIsUnread_NamesTheJoin()
    {
        var reported = HostDiagnostics(ReadsTheRoomAndTheAmenity, OwnerWithAJoin)
            .Where(d => d.Id == "PRAG0639")
            .Select(d => d.GetMessage())
            .ToList();

        reported.Should().Contain(m => m.Contains("RoomAmenity"),
            "the join entity is a second type the reader does not have, and the target alone never names it");
    }

    /// <summary>
    ///     The control the consumer wrote for me: a self-referential many-to-many is silent.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The reading context drops those navigations by name — it is what made <c>[ReadAccess]</c>
    ///     on such an entity usable at all (REGISTRO 0106) — so nothing can name them and there is
    ///     nothing to declare. Reporting it would ask the author for a join entity no query will ever
    ///     reach, and under <c>--warnaserror</c> that is a build broken by a warning about a case the
    ///     framework has already handled. Found the moment the diagnostic became a warning and the
    ///     consumer built against it.
    /// </remarks>
    [Fact]
    public void ASelfReferentialManyToMany_IsNotReported()
    {
        var reported = HostDiagnostics(ReadsTheSelfRelated, OwnerWithASelfRelation)
            .Where(d => d.Id == "PRAG0639")
            .Select(d => d.GetMessage())
            .ToList();

        reported.Should().NotContain(m => m.Contains("TermRelation"),
            "the reading context drops that navigation, so there is nothing for the author to add");
    }

    /// <summary>
    ///     And the model is still generated: this reports, it does not refuse.
    /// </summary>
    /// <remarks>
    ///     A warning rather than an error, because the reader may never name the navigation — and an
    ///     application that compiles today must not stop compiling for a query nobody wrote.
    /// </remarks>
    [Fact]
    public void TheReadingContext_IsStillGenerated()
    {
        var result = Run(ReadsTheChildOnly);

        GeneratorTestHelper.GetGeneratedSource(result, "DbContext.Work")
            .Should().NotBeNull("the diagnostic reports; it does not withhold the context");
    }

    private static IReadOnlyList<Diagnostic> HostDiagnostics(string readingModule, string? ownerModule = null)
        => [.. GeneratorTestHelper.GetGeneratorDiagnostics(Run(readingModule, ownerModule))];

    private static SourceGenRunResult Run(string readingModule, string? ownerModule = null)
    {
        var owner = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module", ownerModule ?? OwnerModule, References);

        var reader = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Work.Module", readingModule, [.. References, owner]);

        return GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [.. References, owner, reader]);
    }

    /// <summary>
    ///     The same set <c>ReadAccessAcrossAssembliesTests</c> uses: a reference short of it makes the
    ///     owner assembly fail to compile, and the case then measures the harness.
    /// </summary>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.ReadAccessAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.Linq.Queryable",
            "System.ComponentModel.Annotations",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
