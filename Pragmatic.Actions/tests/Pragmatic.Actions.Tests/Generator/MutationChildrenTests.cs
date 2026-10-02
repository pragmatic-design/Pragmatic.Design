using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A mutation that carries the children of its aggregate.
/// </summary>
/// <remarks>
///     <para>
///         Matched as a plain property, the children would pair with the entity's navigation of the
///         same name and the auto-map would emit <c>entity.SetLines(this.Lines)</c> — a
///         <c>List&lt;LineItemDto&gt;</c> assigned to an <c>ICollection&lt;LineItem&gt;</c>, which is a
///         compile error inside a file the author cannot open.
///     </para>
///     <para>
///         Whether the parent may write the child is not derivable. <c>Invoice</c> and <c>Property</c>
///         declare the same <c>[Relation.OneToMany]</c>, yet a line item exists only inside its invoice
///         while a room type has its own mutations and its own permission. The child states it with
///         <c>[PartOf&lt;TParent&gt;]</c>, and without that statement the answer is no.
///     </para>
/// </remarks>
public class MutationChildrenTests : ActionsGeneratorTestBase
{
    /// <summary>
    ///     Turns the persistence half of the generator on for a fixture.
    /// </summary>
    /// <remarks>
    ///     <c>FeatureDetector</c> looks this type up by metadata name, and finds it in source as
    ///     readily as in a referenced assembly. Declared here rather than referenced because
    ///     <c>Pragmatic.Persistence.EFCore</c> is not on this test project — and without it the entity
    ///     generator never runs, so the setters and <c>Id</c> that <c>ApplyToEntity</c> writes against
    ///     would not exist and the compile check would be measuring the fixture's references instead
    ///     of the code under test.
    /// </remarks>
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="lineItemAttributes">What the child entity declares about itself.</param>
    /// <param name="lineDtoBody">The element DTO's properties — its key, or the lack of one.</param>
    /// <param name="lineEntityExtra">Extra members on the child entity, e.g. a <c>[LogicKey]</c>.</param>
    /// <param name="extraTypes">Further declarations, for a grandchild and the DTO that writes it.</param>
    /// <param name="childAsDto">
    ///     Declare the child as a <c>[MapTo]</c> DTO instead of a mutation. Only
    ///     <see cref="ADtoChildInsideAMutation_IsRefused" /> wants this: it is the shape
    ///     <c>PRAG0442</c> refuses.
    /// </param>
    private static string Source(
        string lineItemAttributes = "[PartOf<Order>]",
        string lineDtoBody = "public Guid Id { get; init; }\n    public string Description { get; init; } = \"\";",
        string lineEntityExtra = "",
        string extraTypes = "",
        bool childAsDto = false)
    {
        // What a mutation nests is a mutation. It needs no mapping attribute — ApplyToEntity is
        // virtual on Mutation<TEntity> — and no [Endpoint], which is what keeps it a child rather
        // than a second way to address the row.
        var childType = childAsDto ? "LineItemDto" : "WriteLineItemMutation";
        var childDeclaration = childAsDto
            ? $"[MapTo<LineItem>]\npublic partial class LineItemDto\n{{\n    {lineDtoBody}\n}}"
            : "[Mutation(Mode = MutationMode.Update, Internal = true)]\n"
              + $"public partial class WriteLineItemMutation : Mutation<LineItem>\n{{\n    {lineDtoBody}\n}}";

        return Compose(lineItemAttributes, lineEntityExtra, extraTypes, childType, childDeclaration);
    }

    private static string Compose(
        string lineItemAttributes,
        string lineEntityExtra,
        string extraTypes,
        string childType,
        string childDeclaration) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        // IEntity is declared, not left to the generator: the mutation transform reads the id
        // type off the interface, and a generator does not see the interface another file is about to
        // add. Without it LoadEntityAsync is generated as Task.FromResult(null).
        public partial class Order : IEntity
        {
            // private set is what makes the generator emit SetReference — a public setter is written
            // directly and needs none.
            public string Reference { get; private set; } = "";
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        {{lineItemAttributes}}
        public partial class LineItem : IEntity
        {
            public string Description { get; set; } = "";
            {{lineEntityExtra}}
        }

        {{extraTypes}}

        {{childDeclaration}}

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public string? Reference { get; init; }
            public List<{{childType}}> Lines { get; init; } = new();
        }
        """;

    // ── The nested write ─────────────────────────────────────────────────────

    /// <summary>
    ///     A child declared part of the aggregate is merged into it, in the parent's transaction.
    /// </summary>
    [Fact]
    public void AChildOfTheAggregate_IsMergedIntoIt()
    {
        var result = RunGeneratorWithChildren(Source());

        NoErrorsIn(result, "UpdateOrderMutation.Mapping");

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("MutationHelpers.MapOneToMany(");
        generated.Should().Contain("global::TestApp.LineItem.Create()",
            "a child mutation has no ToEntity(): the child is built the way the invoker builds the "
            + "root — through the generated factory, so the entity's own defaults apply — and then "
            + "filled by the same ApplyToEntity that updates an existing one");
        generated.Should().Contain("(d, e) => d.ApplyToEntity(e)",
            "and an existing child is written through its own mutation — the call that carries its "
            + "validation and permissions, which a DTO child could never do");
    }

    /// <summary>
    ///     A mutation is a full representation of the state it carries, so absent children go.
    /// </summary>
    [Fact]
    public void AMutationIsAFullRepresentation_SoTheCollectionIsSynced()
    {
        var result = RunGeneratorWithChildren(Source());

        GetGeneratedSource(result, "UpdateOrderMutation.Mapping").Should()
            .Contain("CollectionStrategy.Sync");
    }

    /// <summary>
    ///     The scalar properties are still assigned through the generated setters, before the children.
    /// </summary>
    /// <remarks>
    ///     Order matters: a child's merge may depend on a scalar this same mutation is setting.
    /// </remarks>
    [Fact]
    public void TheScalarsAreStillAssigned_AndComeFirst()
    {
        var result = RunGeneratorWithChildren(Source());

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping")!;
        var reference = generated.IndexOf("SetReference", StringComparison.Ordinal);
        var lines = generated.IndexOf("MapOneToMany", StringComparison.Ordinal);

        reference.Should().BeGreaterThan(-1);
        lines.Should().BeGreaterThan(reference);
    }

    /// <summary>
    ///     And the child is not reported as a property with no setter.
    /// </summary>
    /// <remarks>
    ///     Reported that way it would be PRAG0414, whose advice is to add a
    ///     <c>SetLines(List&lt;LineItemDto&gt;)</c> to the entity, which is not a thing anyone should do.
    /// </remarks>
    [Fact]
    public void AChildIsNotReportedAsAnUnmappedProperty()
    {
        var result = RunGeneratorWithChildren(Source());

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0414")
            .Any(d => d.GetMessage().Contains("Lines"))
            .Should().BeFalse();
    }

    /// <summary>
    ///     The load brings the children the merge is about to decide over.
    /// </summary>
    /// <remarks>
    ///     Not an optimisation. A keyed merge works out what to remove by looking at what is there, so
    ///     against a collection nobody loaded it removes nothing and adds everything — one duplicate
    ///     per element, on the first update, with a clean build and no diagnostic.
    /// </remarks>
    [Fact]
    public void TheLoadBringsTheChildrenTheMergeWillDecideOver()
    {
        var result = RunGeneratorWithChildren(Source());

        GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker").Should()
            .Contain("Include(query, \"Lines\")");
    }

    /// <summary>
    ///     The load reaches the grandchildren too, not just the children.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A keyed merge decides what to remove by looking at what is there, and EF cannot tell
    ///     an empty collection from one nobody loaded. The rule was applied to the children and stopped
    ///     there; a child DTO carrying children of its own merges them the same way, one level down,
    ///     against a collection the load never populated. Measured against PostgreSQL before this
    ///     existed: two rows became four on a write that sent the same two back, with no error.
    /// </remarks>
    [Fact]
    public void TheLoadReachesTheChildrenOfTheChildren()
    {
        var result = RunGeneratorWithChildren(Source(
            lineDtoBody: "public Guid Id { get; init; }\n"
                         + "    public string Description { get; init; } = \"\";\n"
                         + "    public List<AllocationDto> Allocations { get; init; } = new();",
            lineEntityExtra: "public ICollection<Allocation> Allocations { get; set; } "
                             + "= new List<Allocation>();",
            extraTypes: """
                [Entity]
                [BelongsTo<SalesBoundary>]
                [PartOf<LineItem>]
                public partial class Allocation : IEntity
                {
                    public string Code { get; set; } = "";
                }

                [MapTo<Allocation>]
                public partial class AllocationDto
                {
                    public Guid Id { get; init; }
                    public string Code { get; init; } = "";
                }
                """));

        var source = GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker") ?? "";

        source.Should().Contain("Include(query, \"Lines\")",
            "the child itself is loaded, as before");
        source.Should().Contain("WriteLineItemMutation.WrittenNavigations",
            "and what the child writes in turn is loaded from the paths the child MUTATION publishes. "
            + "A mutation emits its own WrittenNavigations for exactly this: it is the child shape, so "
            + "it owes the parent the same contract a [MapTo] DTO used to. Naming a list that is not "
            + "emitted produces CS0117 in a file the author cannot open, which a text comparison does "
            + "not catch — the build in this fixture does");
        source.Should().Contain("\"Lines.\" + __deep",
            "prefixed by the navigation that reaches the child");
    }

    /// <summary>
    ///     A child the generator refuses to write is not loaded either.
    /// </summary>
    [Fact]
    public void AChildItWillNotWriteIsNotLoaded()
    {
        var result = RunGeneratorWithChildren(Source(lineItemAttributes: ""));

        (GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker") ?? "").Should()
            .NotContain("Include(query, \"Lines\")");
    }

    /// <summary>
    ///     An entity that declares only <c>[Entity]</c> can still be loaded by id.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The id type cannot be read only from the entity interface among the declared interfaces:
    ///         the generator itself adds it, so in the compilation that declares the entity it is not
    ///         there. Read that way, every entity whose author had not also written <c>: IEntity</c> by
    ///         hand would get <c>LoadEntityAsync</c> generated as <c>Task.FromResult(null)</c>: the
    ///         operation would match nothing on every call, with a clean build.
    ///     </para>
    ///     <para>
    ///         And nothing would say so. <c>PRAG0435</c> exists for exactly this — a mutation that
    ///         cannot address its row — and cannot fire when there is no id type to check against.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AnEntityDeclaredOnlyByItsAttribute_IsStillLoadedById()
    {
        var result = RunGeneratorWithChildren("""
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            namespace Pragmatic.Persistence.EFCore
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class PragmaticDbContextAttribute : Attribute { }
            }

            namespace TestApp;

            public class SalesBoundary;

            // No ": IEntity" — the attribute alone, which is what the documentation shows.
            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Ticket
            {
                public string Subject { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateTicketMutation : Mutation<Ticket>
            {
                public Guid Id { get; init; }
                public string? Subject { get; init; }
            }
            """);

        var generated = GetGeneratedSource(result, "UpdateTicketMutation.MutationInvoker");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("Task.FromResult<global::TestApp.Ticket?>(null)",
            "a load that returns null on every call is an operation that finds nothing, forever");
        generated.Should().Contain("GetByIdAsync(mutation.Id",
            "the row is addressed by the id the attribute declares");
    }

    /// <summary>
    ///     A property written the ordinary way is assigned, not routed through a setter that does not exist.
    /// </summary>
    /// <remarks>
    ///     <c>Set{Name}</c> is generated only where the setter is <b>not</b> public — it is the
    ///     change-tracking wrapper for what the outside cannot reach. An auto-map that called it for every
    ///     mapped property would make an entity declared with <c>{ get; set; }</c> produce a call to a
    ///     method nobody writes. The reference application and the other fixtures use <c>private set</c>
    ///     throughout, so only this case exercises a public setter.
    /// </remarks>
    [Fact]
    public void APublicSetter_IsAssignedRatherThanWrapped()
    {
        var result = RunGeneratorWithChildren("""
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            namespace Pragmatic.Persistence.EFCore
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class PragmaticDbContextAttribute : Attribute { }
            }

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Ticket : IEntity
            {
                public string Subject { get; set; } = "";
                public string? Note { get; set; }
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateTicketMutation : Mutation<Ticket>
            {
                public Guid Id { get; init; }
                public string? Subject { get; init; }
                public string? Note { get; init; }
            }
            """);

        var generated = GetGeneratedSource(result, "UpdateTicketMutation.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("SetSubject",
            "no such method is generated for a public setter");
        generated.Should().Contain("entity.Subject = this.Subject;");
        generated.Should().Contain("entity.Note = this.Note;");
    }

    /// <summary>
    ///     The navigation the child is written to is usually one no one declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A relation is stated once and both sides get their member from it, so
    ///         <c>[Relation.OneToMany&lt;LineItem&gt;]</c> on the order is what gives the order its
    ///         <c>LineItems</c>. Reading only the declared properties found nothing and the child was
    ///         dropped — the operation compiled, the endpoint answered 200, and the set the caller sent
    ///         came back unchanged.
    ///     </para>
    ///     <para>
    ///         The fixture above declares its collection by hand, which is why it passed while this did
    ///         not: it was exercising the case that already worked. Found by carrying children in a
    ///         consumer, where every navigation is generated.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AChildIsWrittenToANavigationTheRelationCreated()
    {
        var result = RunGeneratorWithChildren($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;

            {{EfCorePresence}}

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            // Declared here and nowhere else — the collection below is written by another generator.
            [Relation.OneToMany<LineItem>]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [PartOf<Order>]
            public partial class LineItem : IEntity
            {
                public string Description { get; private set; } = "";
            }

            [MapTo<LineItem>]
            public partial class LineItemDto
            {
                public Guid Id { get; init; }
                public string Description { get; init; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateOrderMutation : Mutation<Order>
            {
                public Guid Id { get; init; }
                public List<LineItemDto> LineItems { get; init; } = new();
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0439").Should().BeFalse(
            "the navigation exists — it is simply written by the generator that owns the relation");

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");
        generated.Should().NotBeNull(
            "an operation whose whole payload is a collection still has something to write");
        generated!.Should().Contain("MutationHelpers.MapOneToMany(");
        generated.Should().Contain("d => d.Id");

        GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker").Should()
            .Contain("Include(query, \"LineItems\")");
    }

    /// <summary>
    ///     And when there is genuinely no navigation, the build says so.
    /// </summary>
    /// <remarks>
    ///     The silent skip is what let a curated set come back unchanged from an operation that had
    ///     answered 200. A name that matches nothing is a mistake, and it is now one the compiler makes.
    /// </remarks>
    [Fact]
    public void AChildWithNoNavigationToWriteTo_IsReported()
    {
        var result = RunGeneratorWithChildren($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;

            {{EfCorePresence}}

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.OneToMany<LineItem>]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [PartOf<Order>]
            public partial class LineItem : IEntity
            {
                public string Description { get; private set; } = "";
            }

            [MapTo<LineItem>]
            public partial class LineItemDto
            {
                public Guid Id { get; init; }
                public string Description { get; init; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateOrderMutation : Mutation<Order>
            {
                public Guid Id { get; init; }

                // Named after nothing the order has: the relation produced LineItems.
                public List<LineItemDto> Sightings { get; init; } = new();
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0439").Should().BeTrue(
            "the entity has no navigation called Sightings, so the children have nowhere to go");
    }

    // ── Fail-closed: what is not declared a child is not written ─────────────

    /// <summary>
    ///     Without the declaration the parent may not write it, and the build says so.
    /// </summary>
    /// <remarks>
    ///     The alternative — guessing from the relation — would have made the parent a way around the
    ///     child's own permission, which is the fail-open shape this codebase has shipped before.
    /// </remarks>
    [Fact]
    public void AChildThatNeverSaidItIsOne_IsRefused()
    {
        var result = RunGeneratorWithChildren(Source(lineItemAttributes: ""));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0436").Should().BeTrue();
        (GetGeneratedSource(result, "UpdateOrderMutation.Mapping") ?? "")
            .Should().NotContain("MapOneToMany");
    }

    /// <summary>
    ///     Part of some other aggregate is not part of this one.
    /// </summary>
    [Fact]
    public void AChildOfAnotherAggregate_IsRefused()
    {
        var result = RunGeneratorWithChildren(Source(lineItemAttributes: "[PartOf<SalesBoundary>]"));

        var diagnostic = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0436").FirstOrDefault();
        diagnostic.Should().NotBeNull();
        diagnostic!.GetMessage().Should().Contain("Order",
            "naming the aggregate it would have to belong to is what makes the message actionable");
    }

    /// <summary>
    ///     Elements with no key cannot be matched, so the merge is refused rather than guessed.
    /// </summary>
    [Fact]
    public void ElementsWithNothingToMatchBy_AreRefused()
    {
        var result = RunGeneratorWithChildren(Source(
            lineDtoBody: "public string Description { get; init; } = \"\";"));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0437").Should().BeTrue();
    }

    /// <summary>
    ///     The child's domain key is enough — the DTO need not carry a database id.
    /// </summary>
    [Fact]
    public void ElementsMatchedByTheChildsLogicKey_AreAccepted()
    {
        var result = RunGeneratorWithChildren(Source(
            lineDtoBody: "public string Sku { get; init; } = \"\";\n    public string Description { get; init; } = \"\";",
            lineEntityExtra: "[LogicKey] public string Sku { get; set; } = \"\";"));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0437").Should().BeFalse();

        var generated = GetGeneratedSource(result, "UpdateOrderMutation.Mapping")!;
        generated.Should().Contain("d => d.Sku");
        generated.Should().Contain("e => e.Sku");
    }

    // ── The two declarations cannot contradict each other ────────────────────

    /// <summary>
    ///     An entity written through its parent cannot also be <b>exposed</b> as an operation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both statements are the author's own, so the generator reports the pair instead of
    ///         picking one — there is no reading of the two together that is not a mistake in one.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Exposed</b> is the whole condition: a diagnostic firing on any mutation over a
    ///         <c>[PartOf]</c> entity would forbid the very shape a parent is supposed to nest. A child mutation carries its own validation and permissions — that is
    ///         the reason this check exists, satisfied rather than circumvented. See
    ///         <see cref="AnInternalMutationOnAChild_IsTheShapeAParentNests" />.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AnExposedMutationOnAChildContradictsItsOwnDeclaration()
    {
        var result = RunGeneratorWithChildren($$"""
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            {{EfCorePresence}}

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [PartOf<Order>]
            public partial class LineItem : IEntity
            {
                public string Description { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Update, Internal = false)]
            public partial class UpdateLineItemMutation : Mutation<LineItem>
            {
                public Guid Id { get; init; }
                public string? Description { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0438").Should().BeTrue();
    }

    /// <summary>
    ///     The same mutation, not exposed, is the shape a parent nests — and is allowed.
    /// </summary>
    /// <remarks>
    ///     The only difference from the case above is <c>Internal</c>. A child mutation has no
    ///     endpoint, so it is reached only through its parent: it is not a second way to address the
    ///     row, it is the way the parent writes it — with the child's own validation and permissions
    ///     running, which is exactly what a DTO child could never do.
    /// </remarks>
    [Fact]
    public void AnInternalMutationOnAChild_IsTheShapeAParentNests()
    {
        var result = RunGeneratorWithChildren($$"""
            using System;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            {{EfCorePresence}}

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [PartOf<Order>]
            public partial class LineItem : IEntity
            {
                public string Description { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Update, Internal = true)]
            public partial class WriteLineItemMutation : Mutation<LineItem>
            {
                public Guid Id { get; init; }
                public string? Description { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0438").Should().BeFalse(
            "a child is written through a mutation of its own, which is the reason "
            + "PRAG0438 exists — not the thing it forbids");
    }

    /// <summary>
    ///     ⚠️ Two children of the same aggregate, swapped: the navigation's type tells them apart.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[PartOf]</c> answers «who may write it», not «which navigation it goes into».
    ///         <c>LineItem</c> and <c>Discount</c> both declare <c>[PartOf&lt;Order&gt;]</c>, so the first
    ///         question does not tell them apart: only the type the navigation holds does.
    ///     </para>
    ///     <para>
    ///         Without this check the template would assign an entity into another's collection and
    ///         produce <c>CS0411</c> inside a generated file.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AChildWrittenIntoTheWrongNavigation_IsReported()
    {
        var result = RunGeneratorWithChildren($$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Persistence.Entity;

            {{EfCorePresence}}

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
                public ICollection<Discount> Discounts { get; set; } = new List<Discount>();
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [PartOf<Order>]
            public partial class LineItem : IEntity
            {
                public string Description { get; set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            [PartOf<Order>]
            public partial class Discount : IEntity
            {
                public string Code { get; set; } = "";
            }

            [Mutation(Mode = MutationMode.Update, Internal = true)]
            public partial class WriteDiscountMutation : Mutation<Discount>
            {
                public Guid Id { get; init; }
                public string Code { get; init; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            public partial class UpdateOrderMutation : Mutation<Order>
            {
                public Guid Id { get; init; }
                public List<WriteDiscountMutation> Lines { get; init; } = new();   // navigazione sbagliata
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0443").Should().BeTrue(
            "both are [PartOf<Order>], so only the navigation's type tells them apart");

        GeneratorTestHelper.GetCompilationErrors(result)
            .Should().NotContain(d => d.Id == "CS0411",
                "and the defect does not arrive as a compiler error inside a generated file");
    }

    /// <summary>
    ///     ⚠️ Inside a mutation, what nests is a mutation. A child DTO is refused.
    /// </summary>
    /// <remarks>
    ///     A DTO is shape without behaviour: no <c>ApplyAsync</c>, no validator of its own, no
    ///     <c>[RequirePermission]</c>. Writing a child through one bypasses every rule the child would
    ///     have applied.
    /// </remarks>
    [Fact]
    public void ADtoChildInsideAMutation_IsRefused()
    {
        var result = RunGeneratorWithChildren(Source(childAsDto: true));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0442").Should().BeTrue(
            "a DTO does not carry the child's rules, so it cannot be the child");

        GeneratorTestHelper.HasDiagnostic(RunGeneratorWithChildren(Source()), "PRAG0442")
            .Should().BeFalse("while the same shape with a mutation child is the correct one");
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Asserts the named generated file compiles.
    /// </summary>
    /// <remarks>
    ///     Scoped to that file: the same run also generates repositories over EF Core, whose reference
    ///     closure this fixture does not carry. What matters here is real — a call to a member that
    ///     does not exist is reported at the location of the file that made it.
    /// </remarks>
    private static void NoErrorsIn(SourceGenRunResult result, string hint)
    {
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains(hint) == true)
            .ToList();

        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }

    /// <summary>
    ///     A child that is one, not many, is written through the same door.
    /// </summary>
    /// <remarks>
    ///     <c>ChildWritingTemplate</c> has emitted <c>MapOneToOne</c> for a reference navigation since
    ///     the collection work landed, and nothing asserted it: the helper had unit tests, the
    ///     generator's use of it had none. The two halves that matter are here — an absent child is
    ///     built rather than skipped, and a present one is updated in place rather than replaced,
    ///     which is what keeps its identity and its audit columns.
    /// </remarks>
    [Fact]
    public void AChildThatIsOneNotMany_IsWrittenThroughMapOneToOne()
    {
        var result = RunGeneratorWithChildren(OneToOneSource());

        NoErrorsIn(result, "SetOrderAddressMutation.Mapping");

        var generated = GetGeneratedSource(result, "SetOrderAddressMutation.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("MutationHelpers.MapOneToOne(");
        generated.Should().Contain("global::TestApp.Address.Create()",
            "an absent child is built rather than left absent, and through the factory");
        generated.Should().Contain("(d, e) => d.ApplyToEntity(e)",
            "a present one is corrected in place — a replacement would carry a new identity — and "
            + "through the child's own mutation");
    }

    /// <summary>
    ///     A child that carries children of its own is written all the way down.
    /// </summary>
    /// <remarks>
    ///     Composition rather than a second mechanism: the parent's merge calls the child's
    ///     <c>ToEntity</c> and <c>ApplyTo</c>, and those are the mapping's own — which already write a
    ///     collection through <c>MapOneToMany</c>. Asserted because "it should follow" is how a depth
    ///     limit goes unnoticed until an aggregate has three levels.
    /// </remarks>
    [Fact]
    public void AChildWithChildrenOfItsOwn_IsWrittenAllTheWayDown()
    {
        var result = RunGeneratorWithChildren(GrandchildSource());

        NoErrorsIn(result, "UpdateOrderMutation.Mapping");

        var parent = GetGeneratedSource(result, "UpdateOrderMutation.Mapping");
        parent.Should().NotBeNull();
        parent!.Should().Contain("MapOneToMany(", "the parent merges its own children");

        // The depth is the child DTO's own write, which the parent reaches through d.ToEntity().
        var child = GetGeneratedSource(result, "LineItemDto.Mapping");
        child.Should().NotBeNull();
        child!.Should().Contain("MapOneToMany(",
            "and the child writes its own, so the merge reaches the third level through it");
    }

    /// <summary>An aggregate three levels deep: order, its lines, and each line's allocations.</summary>
    private static string GrandchildSource() => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity] [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
        }

        [Entity] [BelongsTo<SalesBoundary>] [PartOf<Order>]
        public partial class LineItem : IEntity
        {
            public string Description { get; set; } = "";
            public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();
        }

        [Entity] [BelongsTo<SalesBoundary>] [PartOf<LineItem>]
        public partial class Allocation : IEntity
        {
            public int Quantity { get; set; }
        }

        [MapTo<Allocation>]
        public partial class AllocationDto
        {
            public Guid Id { get; init; }
            public int Quantity { get; init; }
        }

        [MapTo<LineItem>]
        public partial class LineItemDto
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
            public List<AllocationDto> Allocations { get; init; } = new();
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public List<LineItemDto> Lines { get; init; } = new();
        }
        """;

    /// <summary>An aggregate whose child is a single reference navigation, not a collection.</summary>
    private static string OneToOneSource() => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Address? ShippingAddress { get; set; }
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class Address : IEntity
        {
            public string Street { get; set; } = "";
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteAddressMutation : Mutation<Address>
        {
            public Guid Id { get; init; }
            public string Street { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class SetOrderAddressMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public WriteAddressMutation? ShippingAddress { get; init; }
        }
        """;

    private static SourceGenRunResult RunGeneratorWithChildren(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);
    }
}
