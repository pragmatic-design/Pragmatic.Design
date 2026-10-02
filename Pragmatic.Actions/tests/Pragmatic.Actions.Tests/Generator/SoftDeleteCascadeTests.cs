using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[SoftDelete(Cascade = true)]</c> reaching a child that declares <c>[SoftDelete]</c>.
/// </summary>
/// <remarks>
///     <para>
///         The cascade looked for children whose type <c>ISoftDelete</c> appears in
///         <c>AllInterfaces</c>. An entity that declares <c>[SoftDelete]</c> receives that interface
///         from a partial <b>the generator itself emits</b>, and a generator cannot see another's output
///         in the same compilation — so the list was empty, the generated <c>DeleteEntity</c> carried no
///         cascade, and the attribute did nothing at all.
///     </para>
///     <para>
///         ⚠️ <b>Why no test caught it.</b> The existing soft-delete tests write
///         <c>class Order : IEntity, ISoftDelete</c> with the three members by hand — the one
///         shape where <c>AllInterfaces</c> already contains it. They were green, and they were green on
///         a shape the documentation does not ask anyone to write.
///     </para>
///     <para>
///         Found by a consumer application: retiring a glossary term left every mention of it live and
///         queryable, with the attribute in place on both sides.
///     </para>
/// </remarks>
public class SoftDeleteCascadeTests : ActionsGeneratorTestBase
{
    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp
        {
            // Declared the documented way: the attribute, and no hand-written interface.
            [SoftDelete]
            public partial class Mention : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Surface { get; set; } = "";
            }

            [SoftDelete(Cascade = true)]
            [Relation.OneToMany<Mention>]
            public partial class Term : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Text { get; set; } = "";
            }

            [Mutation(Mode = MutationMode.Delete)]
            public partial class RetireTerm : Mutation<Term>
            {
                public required Guid Id { get; init; }
            }
        }
        """;

    /// <summary>The cascade reaches a child declared with the attribute.</summary>
    [Fact]
    public void ACascadeReachesAChildDeclaredByAttribute()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "MutationInvoker");

        invoker.Should().Contain("Mentions",
            "the child is [SoftDelete]; the cascade has to walk the relation's navigation to it");
    }

    /// <summary>And it marks them, rather than merely naming them.</summary>
    /// <remarks>
    ///     ⚠️ The obvious assertion — <c>Contain("IsDeleted = true")</c> — is green with the cascade
    ///     removed, because the parent's own <c>DeleteEntity</c> contains that line. Measured: with the
    ///     enricher reverted this test stayed green while the one above went red. The cascade writes
    ///     <c>item.</c>, the parent writes <c>entity.</c>, and only the first distinguishes them.
    /// </remarks>
    [Fact]
    public void TheCascadeMarksTheChildrenDeleted()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "MutationInvoker");

        invoker.Should().Contain("item.IsDeleted = true",
            "a cascade that names the navigation and sets nothing is the same as no cascade");
    }

    private const string WithRestore = """

        namespace TestApp
        {
            [Mutation(Mode = MutationMode.Restore)]
            public partial class ReinstateTerm : Mutation<Term>
            {
                public required Guid Id { get; init; }
            }
        }
        """;

    /// <summary>
    ///     The delete leaves alone a child that was already deleted on its own.
    /// </summary>
    /// <remarks>
    ///     Overwriting it would destroy the only evidence that it went earlier — its own
    ///     <c>DeletedAt</c> — and the restore below would then bring it back with the rest.
    /// </remarks>
    [Fact]
    public void TheCascadeSkipsAChildAlreadyDeleted()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "MutationInvoker");

        invoker.Should().Contain("if (item.IsDeleted)",
            "a child hidden before the parent keeps its own stamp, which is what makes the restore exact");
    }

    /// <summary>
    ///     One instant for the whole cascade.
    /// </summary>
    /// <remarks>
    ///     A per-child <c>UtcNow</c> differs by ticks, and the restore would have nothing to match on.
    /// </remarks>
    [Fact]
    public void TheCascadeStampsEveryChildWithTheSameInstant()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source), "MutationInvoker");

        // The clock is injected, not the static one: an application that pins its clock has to get
        // a DeletedAt that obeys it, and generated code naming DateTimeOffset.UtcNow is the one
        // thing no container can replace.
        invoker.Should().Contain("var deletedAt = _timeProvider.GetUtcNow();")
            .And.Contain("item.DeletedAt = deletedAt;",
                "the children carry the parent's instant, not one of their own");
    }

    /// <summary>
    ///     The restore undoes the cascade — and only what the cascade did.
    /// </summary>
    /// <remarks>
    ///     Without the second half a restore resurrects children somebody had deleted deliberately,
    ///     which is the reason a restore cascade is usually refused outright rather than built.
    /// </remarks>
    [Fact]
    public void TheRestoreUndoesExactlyWhatTheDeleteHid()
    {
        var invoker = GetGeneratedSource(RunGeneratorWithEntities(Source + WithRestore), "ReinstateTerm");

        invoker.Should().Contain("var hiddenAt = entity.DeletedAt;",
            "read before it is cleared, or there is nothing left to match on");
        invoker.Should().Contain("item.DeletedAt != hiddenAt",
            "a child deleted at another moment was not hidden by this delete and must stay hidden");
        invoker.Should().Contain("item.IsDeleted = false;",
            "and the ones that were hidden by it come back");
    }

    /// <summary>
    ///     A cascade does not cross a many-to-many.
    /// </summary>
    /// <remarks>
    ///     Ownership is what a cascade follows, and a join table is not ownership: the other side
    ///     belongs to everyone who points at it. Deleting one property soft-deleted the shared "Wi-Fi"
    ///     amenity itself and hid it from every other property that had it — seven Showcase tests went
    ///     red the moment the attribute-declared targets above started being found, which is how this
    ///     rule was discovered rather than reasoned.
    /// </remarks>
    [Fact]
    public void ACascadeDoesNotCrossAManyToMany()
    {
        var shared = Source.Replace(
            "[Relation.OneToMany<Mention>]",
            """[Relation.ManyToMany<Mention>.WithNavigation("Mentions", Inverse = "Terms")]""");

        var invoker = GetGeneratedSource(RunGeneratorWithEntities(shared), "MutationInvoker");

        invoker.Should().NotContain("item.IsDeleted = true",
            "the far side of a join table is shared, and hiding it would hide it for everyone");
    }

    /// <summary>
    ///     The same rule from the other end: a <c>ManyToOne</c> is the child's pointer to its parent,
    ///     and deleting a child does not delete what it points at.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Invisible while keys were written by hand: a <c>CategoryId</c> typed as a property was no
    ///     relation, so nothing here saw it. Declaring <c>Property → Category</c> made the category a
    ///     cascade target of deleting a property, and the include of its <c>[NotMapped]</c> lookup
    ///     member a 500 on every delete — eight Showcase tests, the first time the declared form ran.
    /// </remarks>
    [Fact]
    public void ACascadeDoesNotFollowAReferenceToItsParent()
    {
        var reference = Source.Replace(
            "[Relation.OneToMany<Mention>]",
            """[Relation.ManyToOne<Mention>.WithNavigation("Mention")]""");

        var invoker = GetGeneratedSource(RunGeneratorWithEntities(reference), "MutationInvoker");

        invoker.Should().NotContain("\"Mention\"",
            "the parent is not a part of the child, so it is neither loaded for the cascade nor marked");
        invoker.Should().NotContain("item.IsDeleted = true");
    }

    /// <summary>
    ///     A <c>[Lookup]</c> is never a part of anything, and its reference member is <c>[NotMapped]</c>:
    ///     naming it in an include is an EF error at runtime, not a no-op.
    /// </summary>
    [Fact]
    public void ACascadeDoesNotReachALookup()
    {
        var lookup = Source.Replace(
            "public partial class Mention",
            "[Lookup] public partial class Mention");

        lookup.Should().NotBe(Source, "the fixture must have been rewritten for the case to measure anything");

        var invoker = GetGeneratedSource(RunGeneratorWithEntities(lookup), "MutationInvoker");

        invoker.Should().NotContain("Mentions",
            "a lookup row is shared reference data, and its member is not an EF navigation");
    }

    /// <summary>
    ///     A control: without <c>Cascade = true</c> the child is left alone.
    /// </summary>
    /// <remarks>
    ///     Cascading by default would be the opposite defect, and a far worse one: it is the difference
    ///     between hiding two rows and hiding a table.
    /// </remarks>
    [Fact]
    public void WithoutCascade_TheChildIsLeftAlone()
    {
        var withoutCascade = Source.Replace("[SoftDelete(Cascade = true)]", "[SoftDelete]");

        var invoker = GetGeneratedSource(RunGeneratorWithEntities(withoutCascade), "MutationInvoker");

        invoker.Should().NotContain("Mentions",
            "the default is false, and it has to stay false");
    }
}
