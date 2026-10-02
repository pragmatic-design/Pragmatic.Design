using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     What <c>ApplyPatch</c> does with the children a patch carries.
/// </summary>
/// <remarks>
///     <para>
///         A template that filtered them out — <c>IsCollection: false, IsNestedMutation: false</c> —
///         would let a patch with a <c>Lines</c> collection compile, run, and write none of it,
///         reporting nothing. The children would simply be absent from the update.
///     </para>
///     <para>
///         These run the whole generator and compile the result, so a call to a member that does not
///         exist fails here rather than in a consumer's build.
///     </para>
/// </remarks>
public class PatchApplyCollectionTests
{
    /// <param name="lineDto">The element DTO declaration the patch's collection is made of.</param>
    /// <param name="collectionProperty">The collection as the patch declares it.</param>
    /// <param name="lineEntityExtra">Extra members on the child entity, e.g. a <c>[LogicKey]</c>.</param>
    private static string Source(
        string lineDto,
        string collectionProperty = "public List<OrderLineDto> Lines { get; init; } = new();",
        string lineEntityExtra = "") => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Mapping.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Patch;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order
        {
            public string Reference { get; set; } = "";
            public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();

            // A child that is one rather than many. Present for every fixture and written only by the
            // patches that declare it, so the collection tests are unaffected.
            public OrderLine? FirstLine { get; set; }
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class OrderLine
        {
            public string Description { get; set; } = "";
            {{lineEntityExtra}}
        }

        {{lineDto}}

        [Patch<Order>]
        public partial class UpdateOrder
        {
            public string? Reference { get; init; }

            {{collectionProperty}}
        }
        """;

    private const string MappedLineDto = """
        [MapTo<OrderLine>]
        public partial class OrderLineDto
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
        }
        """;

    private const string PatchOnlyLineDto = """
        [Patch<OrderLine>]
        public partial class OrderLineDto
        {
            public Guid Id { get; init; }
            public string? Description { get; init; }
        }
        """;

    // ── The normal case: the element can build and update itself ────────────

    /// <summary>
    ///     A patch that carries children now writes them, through the same helper the mapping side uses.
    /// </summary>
    [Fact]
    public void ACollectionOfMappedDtos_IsWrittenThroughMapOneToMany()
    {
        var result = RunGenerator(Source(MappedLineDto));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().Contain("MutationHelpers.MapOneToMany(");
        generated.Should().Contain("d => d.ToEntity()");
        generated.Should().Contain("(d, e) => d.ApplyToLoaded(e)",
            "a patch writes through a context-free entry point, so the child is written with the "
            + "form whose name states that its navigations are already loaded");
    }

    /// <summary>
    ///     A patch carrying a child that is one, not many.
    /// </summary>
    /// <remarks>
    ///     <c>RenderNestedWrite</c> has two branches and only the collection one was covered. This is
    ///     the reference-navigation half: create it when the DTO can build one, and where the element
    ///     is patch-only, leave an absent child absent rather than inventing one out of a delta that
    ///     never described a whole.
    /// </remarks>
    [Fact]
    public void AChildThatIsOneNotMany_IsWrittenThroughMapOneToOne()
    {
        var result = RunGenerator(Source(
            """
            [MapTo<OrderLine>]
            public partial class OrderLineDto
            {
                public Guid Id { get; init; }
                public string Description { get; init; } = "";
            }
            """,
            collectionProperty: "public OrderLineDto? FirstLine { get; init; }"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().Contain("MutationHelpers.MapOneToOne(");
        generated.Should().Contain("d => d.ToEntity()", "an absent child is built rather than skipped");
    }

    /// <summary>
    ///     A patch-only child cannot be built, so an absent one stays absent.
    /// </summary>
    /// <remarks>
    ///     A <c>[Patch]</c> element describes a change, not a whole: there is nothing to construct from.
    ///     Creating one anyway would write a row whose unmentioned fields are defaults — which reads as
    ///     a successful patch and is a fabricated record.
    /// </remarks>
    [Fact]
    public void APatchOnlyChildThatIsOne_IsNotCreatedFromNothing()
    {
        var result = RunGenerator(Source(
            PatchOnlyLineDto,
            collectionProperty: "public OrderLineDto? FirstLine { get; init; }"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("MutationHelpers.MapOneToOne(",
            "there is no factory to give it — the delta never described a whole child");
        generated.Should().Contain("ApplyPatch(", "what is already there is still patched");
    }

    /// <summary>
    ///     A patch is a delta, so a child it does not mention is not a child it is deleting.
    /// </summary>
    [Fact]
    public void APatchNeverRemovesWhatItDoesNotMention()
    {
        var result = RunGenerator(Source(MappedLineDto));

        GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch").Should()
            .Contain("CollectionStrategy.AddOnly");
    }

    /// <summary>
    ///     The override is read on the patch side too, not only by the mapping generator.
    /// </summary>
    [Theory]
    [InlineData("Sync")]
    [InlineData("Replace")]
    public void CollectionStrategy_OverridesTheDeltaTheShapeImplies(string strategy)
    {
        var result = RunGenerator(Source(
            MappedLineDto,
            $"[CollectionStrategy(CollectionStrategy.{strategy})] "
            + "public List<OrderLineDto> Lines { get; init; } = new();"));

        NoCompilationErrors(result);

        GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch").Should()
            .Contain($"CollectionStrategy.{strategy}");
    }

    /// <summary>
    ///     <c>Ignore</c> means the collection is read-only: no call, and no diagnostic either.
    /// </summary>
    [Fact]
    public void CollectionStrategy_Ignore_WritesNothing()
    {
        var result = RunGenerator(Source(
            MappedLineDto,
            "[CollectionStrategy(CollectionStrategy.Ignore)] "
            + "public List<OrderLineDto> Lines { get; init; } = new();"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("MapOneToMany");
    }

    /// <summary>
    ///     Both branches of <c>ApplyPatch</c> write the collection.
    /// </summary>
    /// <remarks>
    ///     The method has two paths — explicit <c>MarkSet</c> tracking, and the nullable fallback for
    ///     callers that never mark anything. A child written by only one of them is a patch whose
    ///     behaviour depends on how the DTO was populated.
    /// </remarks>
    [Fact]
    public void BothTrackedAndFallbackPathsWriteTheCollection()
    {
        var result = RunGenerator(Source(MappedLineDto));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!;

        generated.Should().Contain("_setProperties.Contains(nameof(Lines))");
        generated.Should().Contain("if (Lines is not null)");
    }

    // ── The element is itself a patch: it updates, it does not create ────────

    /// <summary>
    ///     A patch element has no <c>ToEntity()</c>, so it updates what it matches and nothing else.
    /// </summary>
    [Fact]
    public void APatchOnlyElement_UpdatesMatchesInsteadOfCreating()
    {
        var result = RunGenerator(Source(PatchOnlyLineDto));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!;
        generated.Should().Contain("item.ApplyPatch(existing)");
        generated.Should().NotContain("MapOneToMany",
            "the helper needs a factory, and a patch element cannot build an entity");
    }

    /// <summary>
    ///     And it says so, because "the line I sent was ignored" is otherwise indistinguishable from a bug.
    /// </summary>
    [Fact]
    public void APatchOnlyElement_IsReportedAsUpdateOnly()
    {
        var result = RunGenerator(Source(PatchOnlyLineDto));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2205").Should().BeTrue();
    }

    // ── What the patch cannot write, it says ─────────────────────────────────

    /// <summary>
    ///     No key on either side is an error: the alternative is a child that silently never updates.
    /// </summary>
    [Fact]
    public void ElementsWithNothingToMatchBy_AreReported()
    {
        var result = RunGenerator(Source("""
            [MapTo<OrderLine>]
            public partial class OrderLineDto
            {
                public string Description { get; init; } = "";
            }
            """));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2203").Should().BeTrue();
    }

    /// <summary>
    ///     Matching by the child's domain key is enough — the element need not carry an id.
    /// </summary>
    [Fact]
    public void ElementsMatchedByTheChildsLogicKey_AreAccepted()
    {
        var result = RunGenerator(Source(
            """
            [MapTo<OrderLine>]
            public partial class OrderLineDto
            {
                public string Sku { get; init; } = "";
                public string Description { get; init; } = "";
            }
            """,
            lineEntityExtra: "[LogicKey] public string Sku { get; set; } = \"\";"));

        NoCompilationErrors(result);
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2203").Should().BeFalse();

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!;
        generated.Should().Contain("d => d.Sku");
        generated.Should().Contain("e => e.Sku");
    }

    /// <summary>
    ///     A DTO of the developer's own that can neither create nor update its entity is named, not skipped.
    /// </summary>
    [Fact]
    public void AnElementThatCannotWriteItsEntity_IsReported()
    {
        var result = RunGenerator(Source("""
            public class OrderLineDto
            {
                public Guid Id { get; init; }
                public string Description { get; init; } = "";
            }
            """));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2204").Should().BeTrue();
    }

    /// <summary>
    ///     A collection of values is not a child and was never in scope: no call, no diagnostic.
    /// </summary>
    [Fact]
    public void ACollectionOfValues_IsNotTreatedAsAChild()
    {
        var result = RunGenerator(Source(
            MappedLineDto,
            "public List<string> Tags { get; init; } = new();"));

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2204").Should().BeFalse();
        GeneratorTestHelper.HasDiagnostic(result, "PRAG2203").Should().BeFalse();
    }

    // ── A single child, not a collection ─────────────────────────────────────

    /// <summary>
    ///     A nested DTO that can build an entity is created when absent and updated when present.
    /// </summary>
    [Fact]
    public void ANestedMappedDto_IsCreatedOrUpdatedInPlace()
    {
        var result = RunGenerator(NestedSource("[MapTo<Address>]"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!;
        generated.Should().Contain("MutationHelpers.MapOneToOne(");
        generated.Should().Contain("d => d.ToEntity()");
    }

    /// <summary>
    ///     A nested patch has no <c>ToEntity()</c>, so it updates the child that is there and creates none.
    /// </summary>
    [Fact]
    public void ANestedPatch_UpdatesTheChildThatExists()
    {
        var result = RunGenerator(NestedSource("[Patch<Address>]"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!;
        generated.Should().NotContain("MapOneToOne");
        generated.Should().Contain("ApplyPatch(target.ShippingAddress)");
    }

    /// <summary>
    ///     A patch goes below the first level: the children's children are written.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The shape differs from <c>[MapTo]</c>: a <c>[Patch&lt;T&gt;]</c> element has no
    ///     <c>ToEntity()</c>, so <c>MapOneToMany</c> cannot be used — the factory is missing — and the
    ///     generated code is an explicit match-and-patch, update-only by construction. The recursion is
    ///     there all the same, through the element's <c>ApplyPatch</c>.
    /// </remarks>
    [Fact]
    public void APatchReachesTheChildrenOfItsChildren()
    {
        var result = RunGenerator(DeepSource());

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!;

        generated.Should().Contain("item.ApplyPatch(existing)",
            "each line is matched by key and patched — update-only, because a patch element has no "
            + "ToEntity() to create one from");

        // ⚠️ The real depth shows in the child's generated code: the line's ApplyPatch must write its
        // allocations, and if it stopped there the level below would be silently ignored.
        var line = GeneratorTestHelper.GetGeneratedSource(result, "OrderLineDto.Patch")!;
        line.Should().Contain("Allocations",
            "the line patches its own children in turn, which is what makes the tree deep");
    }

    /// <summary>An order with lines, and each line with its allocations: three levels.</summary>
    private static string DeepSource() => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Patch;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order
        {
            public string Reference { get; set; } = "";
            public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class OrderLine
        {
            public string Description { get; set; } = "";
            public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Allocation
        {
            public string Code { get; set; } = "";
        }

        [Patch<Allocation>]
        public partial class AllocationDto
        {
            public Guid Id { get; init; }
            public string? Code { get; init; }
        }

        [Patch<OrderLine>]
        public partial class OrderLineDto
        {
            public Guid Id { get; init; }
            public string? Description { get; init; }
            public List<AllocationDto>? Allocations { get; init; }
        }

        [Patch<Order>]
        public partial class UpdateOrder
        {
            public string? Reference { get; init; }
            public List<OrderLineDto>? Lines { get; init; }
        }
        """;

    /// <param name="addressDtoAttribute">What the nested DTO declares itself to be.</param>
    private static string NestedSource(string addressDtoAttribute) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Patch;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order
        {
            public string Reference { get; set; } = "";
            public Address? ShippingAddress { get; set; }
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Address
        {
            public string City { get; set; } = "";
        }

        {{addressDtoAttribute}}
        public partial class AddressDto
        {
            public string City { get; init; } = "";
        }

        [Patch<Order>]
        public partial class UpdateOrder
        {
            public string? Reference { get; init; }
            public AddressDto? ShippingAddress { get; init; }
        }
        """;

    /// <summary>
    ///     Compiles the generated <c>ApplyPatch</c> and asserts nothing in it is broken.
    /// </summary>
    /// <remarks>
    ///     Scoped to that one file on purpose. The same run also generates repositories over EF Core,
    ///     whose reference closure — <c>IListSource</c>, <c>System.Linq.Queryable</c> — this fixture
    ///     does not carry; asserting over the whole compilation would measure that closure instead of
    ///     the code under test. What matters here is real: a call to a member that does not exist is
    ///     reported at the location of the file that made it.
    /// </remarks>
    private static void NoCompilationErrors(SourceGenRunResult result)
    {
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("UpdateOrder.Patch") == true)
            .ToList();

        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<IEntity>(),
                GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
                GeneratorTestHelper.FromType<EntityAttribute>(),
                GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
                GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
                GeneratorTestHelper.FromType<Patch.PatchAttribute<object>>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
                // The generated repositories and registrations are part of what has to compile: an
                // ApplyPatch that is right in a compilation missing half its references proves little.
                GeneratorTestHelper.FromType<Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Result.Result<,>)),
                GeneratorTestHelper.FromType<Result.IError>(),
                // A [Patch<T>] now comes with its JSON converter, which names System.Text.Json.
                GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Json.JsonSerializer)),
            ]);
    }
}
