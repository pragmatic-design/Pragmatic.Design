using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     What <c>[LoadWith&lt;T&gt;]</c> counts as a navigation.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="LoadingProfileTemplateTests" /> builds the model by hand, so it can only check
///         that a path it was given is rendered. The paths themselves come from the transform, and that
///         is where the defect was: a second, cruder copy of the entity-side rule treated every generic
///         collection and every non-string reference type as a navigation. A primitive collection (a
///         JSON column) and a value object (flattened into columns) both passed it, and
///         <c>Include(e =&gt; e.Aliases)</c> is rejected by EF Core before it reads a row.
///     </para>
///     <para>
///         The corpus in <see cref="GeneratedSurfaceInventoryTests" /> could not show it: its
///         <c>[LoadWith]</c> entity has a string and two relations and nothing else, so every property
///         it owns happens to be classified correctly. This is the control case it was missing.
///     </para>
/// </remarks>
public class LoadWithNavigationSelectionTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace App.Catalog;

        [ValueObject]
        public partial record Position
        {
            public decimal Latitude { get; init; }
            public decimal Longitude { get; init; }
        }

        [Entity]
        public partial class Tag : IEntity
        {
            public string Label { get; private set; } = "";
        }

        [Entity]
        public partial class Detail : IEntity
        {
            public string Note { get; private set; } = "";

            // Forward, at depth 2: the include the profile has to reach.
            public Guid TagId { get; private set; }
            public Tag Tag { get; private set; } = null!;

            // The inverse of KnowledgeItem.Detail — at depth 2 this is the way back up the include
            // tree, which EF Core refuses.
            public Guid KnowledgeItemId { get; private set; }
            public KnowledgeItem KnowledgeItem { get; private set; } = null!;
        }

        [Entity]
        public partial class KnowledgeItem : IEntity
        {
            public string Title { get; private set; } = "";

            // A JSON column, not a navigation: EF Core cannot Include it.
            public List<string> Aliases { get; private set; } = [];

            // Flattened into columns, not a navigation either.
            public Position Position { get; private set; } = new();

            // The real one — a reference with its foreign key beside it.
            public Guid DetailId { get; private set; }
            public Detail Detail { get; private set; } = null!;
        }

        [LoadWith<KnowledgeItem>(MaxDepth = 2)]
        [Query<KnowledgeItem>]
        public partial class GetKnowledgeItem
        {
            public Guid? Id { get; init; }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        // The profile is emitted only when Persistence.EFCore is visible — the gate is
        // DetectedFeatures.HasPersistenceEFCore in AdvancedFeature. Without this the run produces
        // nothing at all, and every NotContain below would hold on an empty result.
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>(),
    ];

    private static string Profile()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);
        var profile = GeneratorTestHelper.GetGeneratedSource(result, "LoadingProfile");

        profile.Should().NotBeNull(
            "the query carries [LoadWith], so a profile is generated — asserted before the exclusions "
            + "below, because 'the profile does not include Aliases' and 'there is no profile' read the "
            + "same in a NotContain");

        return profile!;
    }

    [Fact]
    public void LoadWith_IncludesTheNavigationThatHasAForeignKey()
    {
        Profile().Should().Contain("Include(e => e.Detail)");
    }

    [Fact]
    public void LoadWith_SkipsAPrimitiveCollection()
    {
        Profile().Should().NotContain("Aliases",
            "a collection of scalars is a JSON column; Include over it throws before a row is read");
    }

    [Fact]
    public void LoadWith_SkipsAValueObject()
    {
        Profile().Should().NotContain("Position",
            "a value object is flattened into the owner's columns — there is nothing to include");
    }

    /// <summary>
    ///     At depth 2 the inverse navigation is the way back up the include tree, and EF Core refuses it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         "The navigation was ignored from 'Include' since the fix-up will automatically populate
    ///         it. […] Walking back include tree is not allowed." A warning by default, an error in any
    ///         application that promotes it — and pointless either way, because the fix-up does populate
    ///         it.
    ///     </para>
    ///     <para>
    ///         It is the same defect as the primitive collection — the profile naming what cannot be
    ///         included — one layer down.
    ///     </para>
    /// </remarks>
    [Fact]
    public void LoadWith_SkipsTheNavigationThatWalksBackUpTheIncludeTree()
    {
        var profile = Profile();

        profile.Should().Contain("ThenInclude(e => e.Tag)",
            "depth 2 still reaches the child's own forward navigation — without this the test would "
            + "pass on a profile that stopped at depth 1, or on one that included nothing");
        profile.Should().NotContain("Detail.KnowledgeItem");
        profile.Should().NotContain("ThenInclude(e => e.KnowledgeItem)");
    }

    /// <summary>
    ///     The profile publishes its paths as strings, which is the shape the read path consumes.
    /// </summary>
    /// <remarks>
    ///     <c>ApplyIncludes()</c> is a typed extension and cannot feed <c>IIncludableQuery</c>: the
    ///     executor's loop calls <c>Include(string)</c>. Publishing the same list as strings is what
    ///     lets the generated query name it, instead of a second mechanism beside the one that runs.
    /// </remarks>
    [Fact]
    public void LoadWith_PublishesItsPathsAsStrings()
    {
        Profile().Should().Contain("IncludePaths").And.Contain("\"Detail\"");
    }

    /// <summary>
    ///     An entity-shaped query with <c>[LoadWith]</c> composes the profile into its
    ///     <c>IncludePaths</c> — the caller the attribute never had.
    /// </summary>
    /// <remarks>
    ///     Entity-shaped on purpose: <c>NeedsEagerLoading</c> is false for a query that projects,
    ///     because a projection turns a navigation into a JOIN and an include would add nothing. It is
    ///     the query answering with the entity whose collections came back empty.
    /// </remarks>
    [Fact]
    public void EntityShapedQueryWithLoadWith_ComposesTheProfileIntoItsIncludePaths()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source, References);
        var query = GeneratorTestHelper.GetGeneratedSource(result, "GetKnowledgeItem.Query");

        query.Should().NotBeNull("[Query<KnowledgeItem>] generates the query partial");
        query!.Should().Contain("IIncludableQuery",
            "without the interface the executor never looks at the paths");
        query.Should().Contain("GetKnowledgeItemLoadingProfile.IncludePaths",
            "the query names the profile written by the same generator in the same compilation");
    }
}
