using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Patch;

/// <summary>
///     A patch over an entity has to carry the columns a <c>[Relation.*]</c> produces, because the
///     author never wrote them and cannot be asked to.
/// </summary>
/// <remarks>
///     <para>
///         A relation is declared once and the generator adds the foreign key. During the patch
///         transform's own pass that name is not a symbol — the generator that creates it has not run
///         yet — so asking the entity for its members does not find it. The patch came out with a
///         branch for every declared property and <b>no line</b> for the relation's key, and nothing
///         said so: no <c>PRAG2203</c>, no <c>PRAG2204</c>, no warning. A mutation naming the same
///         property writes it, so the setter is there; only the patch could not see it.
///     </para>
///     <para>
///         Every other feature facing this asks <c>TraitPropertyResolver</c>, whose own documentation
///         says to use it "whenever the question is: will this name exist on the entity once
///         generation has run". Mapping learned it the same way (PRAG0303, a column dropped from a
///         projection); the patch transform was the last place still asking the symbol.
///     </para>
/// </remarks>
public class RelationForeignKeyOnAPatchTests
{
    private const string Source = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Patch.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp.Work;

        [Boundary]
        public partial class WorkBoundary;

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class WorkItem : IEntity
        {
            public string Title { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<WorkBoundary>]
        [Relation.ManyToOne<WorkItem>.WithNavigation("Parent")]
        public partial class StoryPlacement : IEntity
        {
            public int Rank { get; set; }
        }

        [GeneratePatch<StoryPlacement>]
        public partial record StoryPlacementPatch;
        """;

    private static string ThePatch(string source = Source)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);
        var patch = GeneratorTestHelper.GetGeneratedSource(result, "StoryPlacementPatch.Patch");

        patch.Should().NotBeNull(
            "the patch type is generated at all — otherwise every assertion below is about nothing");

        return patch!;
    }

    /// <summary>The relation's foreign key is patchable like any other column.</summary>
    [Fact]
    public void AForeignKeyAnotherGeneratorProduces_IsPatchable()
    {
        ThePatch().Should().Contain("ParentId",
            "the column a relation produces is a column: the author cannot name it, so the "
            + "generator has to predict it");
    }

    /// <summary>
    ///     The control: a property the author <em>did</em> write is patchable, and always was.
    /// </summary>
    /// <remarks>
    ///     Without it, "the patch mentions ParentId" would be satisfied by a transform that names
    ///     everything it can think of, and by one that emits a patch with nothing usable in it.
    /// </remarks>
    [Fact]
    public void ADeclaredProperty_IsPatchable()
    {
        ThePatch().Should().Contain("Rank");
    }

    /// <summary>And the navigation itself is not patchable — only its key.</summary>
    /// <remarks>
    ///     The second control, and the one that keeps the prediction honest: the predictor offers
    ///     navigations alongside foreign keys, so taking everything it returns would put an entity
    ///     reference on the wire as a correctable field. A patch corrects columns.
    /// </remarks>
    [Fact]
    public void TheNavigationItself_IsNotPatchable()
    {
        ThePatch().Should().NotContain("global::TestApp.Work.WorkItem>",
            "a navigation is not a column, and a patch corrects columns");
    }

    /// <summary>An optional relation's key is nullable in the patch, as it is on the entity.</summary>
    /// <remarks>
    ///     The prediction read <c>IsRequired</c>, which the attribute does not have — it says
    ///     <c>Required</c> — so every predicted key came out required: <c>Optional&lt;Guid&gt;</c> for a
    ///     <c>Guid?</c> column, and a patch that could not clear it. It compiled, because a <c>Guid</c>
    ///     converts to a <c>Guid?</c> on the way to the setter.
    /// </remarks>
    [Fact]
    public void AnOptionalRelationsKey_IsNullableInThePatch()
    {
        var patch = ThePatch(WithAnOptionalReviewer);

        Regex.IsMatch(patch, @"Optional<[\w.:]*Guid\?> ReviewerId").Should().BeTrue(
            "Required = false makes the column nullable, and a patch has to be able to clear it:\n" + patch);
    }

    /// <summary>The control: the required relation's key, in the same patch, is not nullable.</summary>
    /// <remarks>
    ///     Without it, "the key is nullable" is satisfied by making every predicted key nullable — and a
    ///     required column would accept a null it cannot store.
    /// </remarks>
    [Fact]
    public void ARequiredRelationsKey_StaysNotNullable()
    {
        var patch = ThePatch(WithAnOptionalReviewer);

        Regex.IsMatch(patch, @"Optional<[\w.:]*Guid> ParentId").Should().BeTrue(patch);
    }

    private static readonly string WithAnOptionalReviewer = Source.Replace(
        """[Relation.ManyToOne<WorkItem>.WithNavigation("Parent")]""",
        """
        [Relation.ManyToOne<WorkItem>.WithNavigation("Parent")]
        [Relation.ManyToOne<Person>.WithNavigation("Reviewer", Required = false)]
        """) + """

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class Person : IEntity
        {
            public string Name { get; private set; } = "";
        }
        """;

    /// <summary>
    ///     Excluding the predicted key works, and does not read as a typo.
    /// </summary>
    /// <remarks>
    ///     The other half of predicting it. An existence check that asks the symbol alone cannot see
    ///     the predicted key, so <c>[PatchIgnore("ParentId")]</c> would make <c>PRAG2206</c> fire on a
    ///     name that does exist, and tell the author the property is still in the patch when it is not.
    /// </remarks>
    [Fact]
    public void ExcludingThePredictedKey_IsHonoured_AndIsNotReportedAsATypo()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source.Replace(
                "[GeneratePatch<StoryPlacement>]",
                """
                [GeneratePatch<StoryPlacement>]
                [PatchIgnore("ParentId")]
                """),
            References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2206").Should().BeFalse(
            "the name exists — it is a column another generator writes");

        var patch = GeneratorTestHelper.GetGeneratedSource(result, "StoryPlacementPatch.Patch");
        patch.Should().NotBeNull();
        patch!.Should().NotContain("ParentId", "and the exclusion the author asked for is applied");
        patch.Should().Contain("Rank", "while the rest of the patch is untouched");
    }

    /// <summary>
    ///     The control: a name that really does not exist is still reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "no PRAG2206 for ParentId" would be satisfied by deleting the check — which
    ///     would restore the property the attribute was written to protect, in silence.
    /// </remarks>
    [Fact]
    public void ExcludingANameThatDoesNotExist_IsStillReported()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source.Replace(
                "[GeneratePatch<StoryPlacement>]",
                """
                [GeneratePatch<StoryPlacement>]
                [PatchIgnore("NoSuchColumn")]
                """),
            References);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2206").Should().BeTrue();
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Patch.Optional<int>>(),
        GeneratorTestHelper.FromType<global::System.Text.Json.Serialization.JsonConverter<int>>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.ComponentModel.Annotations",
            "System.Linq.Queryable",
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
