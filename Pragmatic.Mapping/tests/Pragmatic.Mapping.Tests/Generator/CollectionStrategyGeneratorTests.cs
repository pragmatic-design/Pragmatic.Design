using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     How a collection of DTOs is written back to the entity: derived from the kind of DTO, overridable
///     per property, and refused at build time when the elements cannot be matched.
/// </summary>
/// <remarks>
///     <para>
///         Before this, <c>ApplyTo</c> emitted
///         <c>entity.Lines = this.Lines.Select(x =&gt; x.ToEntity()).ToList()</c> for every collection:
///         right when creating, and on an update a wholesale replacement that discarded each existing
///         child along with its identity, audit columns and soft-delete state.
///     </para>
///     <para>
///         The runtime that does it properly — <c>MutationHelpers.MapOneToMany</c> — already existed and
///         was called by nobody. These tests pin the wiring and the derivation, not the algorithm: the
///         add/update/remove behaviour itself is covered by <c>MutationHelpersTests</c>.
///     </para>
/// </remarks>
public class CollectionStrategyGeneratorTests : MappingGeneratorTestBase
{
    /// <param name="dtoAttributes">Attributes on the parent DTO — <c>[MapTo]</c>, <c>[Patch]</c>, …</param>
    /// <param name="collectionAttribute">An optional <c>[CollectionStrategy]</c> on the collection.</param>
    /// <param name="lineDtoKey">The key property declared on the element DTO, if any.</param>
    /// <param name="lineEntityKey">The key property declared on the child entity, if any.</param>
    private static string Source(
        string dtoAttributes,
        string collectionAttribute = "",
        string lineDtoKey = "public int Id { get; init; }",
        string lineEntityKey = "public int Id { get; set; }") => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Mapping.Mutation;
        using Pragmatic.Persistence.Patch;

        // Declared here rather than referenced: Pragmatic.Persistence is not on this test project's
        // compilation, and the analyzer matches the attribute by name and namespace.
        namespace Pragmatic.Persistence.Entity
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class LogicKeyAttribute : Attribute { }
        }

        namespace TestApp.Entities
        {
            public class OrderLine
            {
                {{lineEntityKey}}
                public string Description { get; set; } = "";
            }

            public class Order
            {
                public int Id { get; set; }
                public string Reference { get; set; } = "";
                public List<OrderLine> Lines { get; set; } = new();
            }
        }

        namespace TestApp.Dtos
        {
            [MapTo<TestApp.Entities.OrderLine>]
            public partial class OrderLineDto
            {
                {{lineDtoKey}}
                public string Description { get; init; } = "";
            }

            {{dtoAttributes}}
            public partial class UpdateOrderDto
            {
                public string? Reference { get; init; }

                {{collectionAttribute}}
                public List<OrderLineDto> Lines { get; init; } = new();
            }
        }
        """;

    // ── The strategy is derived from the DTO type ────────────────────────────

    /// <summary>
    ///     A full representation replaces the state it describes: absent children are removed.
    /// </summary>
    [Fact]
    public void MapTo_IsAFullRepresentation_SoTheCollectionIsSynced()
    {
        var result = RunGenerator(Source("[MapTo<TestApp.Entities.Order>]"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        generated.Should().Contain("MutationHelpers.MapOneToMany(");
        generated.Should().Contain("CollectionStrategy.Sync");
        ApplyToBody(generated).Should().NotContain("Lines = this.Lines?.Select",
            "a wholesale replacement is what this replaces");
    }

    /// <summary>
    ///     A collection the DTO does not carry is a collection the update does not touch.
    /// </summary>
    /// <remarks>
    ///     The reading a null single navigation already gets one property over — "I am not telling
    ///     you about these". Without the guard the null reaches <c>MapOneToMany</c>, which reads it as an
    ///     empty incoming set, and under <c>Sync</c> an empty incoming set removes every existing child.
    ///     Measured in conformance by <c>TheOmittedCollection</c>; here is the guard that makes it so. An empty list still means
    ///     "none of them": that is the strategy's job, not the guard's.
    /// </remarks>
    [Fact]
    public void ANullCollection_IsNotWritten()
    {
        var result = RunGenerator(Source("[MapTo<TestApp.Entities.Order>]"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        ApplyToBody(GetGeneratedSource(result, "UpdateOrderDto.Mapping")).Should()
            .Contain("if (this.Lines is not null)",
                "absent is not a value, for a collection as for a single navigation");
    }

    /// <summary>
    ///     The body of <c>ApplyTo</c> alone — <c>ToEntity</c> sits in the same file and keeps building
    ///     the collection whole, so asserting over the file would confuse the two.
    /// </summary>
    private static string ApplyToBody(string? generated)
    {
        var start = generated?.IndexOf("public void ApplyTo", StringComparison.Ordinal) ?? -1;
        start.Should().BeGreaterThan(-1, "the update method is what these tests are about");
        return generated![start..];
    }

    /// <summary>
    ///     A patch owns its own update method, so this template stops at the boundary.
    /// </summary>
    /// <remarks>
    ///     <c>[Patch&lt;T&gt;]</c> suppresses <c>ApplyTo</c> here and generates <c>ApplyPatch</c>
    ///     instead. The delta strategy that a patch implies is therefore exercised on that side —
    ///     see <c>PatchApplyCollectionTests</c> in Pragmatic.Persistence.EFCore.Tests.
    /// </remarks>
    [Fact]
    public void Patch_OwnsItsUpdate_SoNoApplyToIsGeneratedHere()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]\n    [Patch<TestApp.Entities.Order>]"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        GetGeneratedSource(result, "UpdateOrderDto.Mapping").Should()
            .NotContain("public void ApplyTo");
    }

    // ── The attribute wins over the derivation ───────────────────────────────

    [Theory]
    [InlineData("Sync")]
    [InlineData("AddOnly")]
    [InlineData("Replace")]
    public void CollectionStrategy_OverridesWhatTheShapeImplies(string strategy)
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            $"[CollectionStrategy(CollectionStrategy.{strategy})]"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        GetGeneratedSource(result, "UpdateOrderDto.Mapping").Should()
            .Contain($"CollectionStrategy.{strategy}");
    }

    /// <summary>
    ///     <c>Ignore</c> leaves no trace: a read-only collection should not appear in the update at all.
    /// </summary>
    [Fact]
    public void CollectionStrategy_Ignore_WritesNothing()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            "[CollectionStrategy(CollectionStrategy.Ignore)]"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        generated.Should().NotContain("MapOneToMany",
            "an ignored collection produces no call, not a call that returns immediately");
    }

    // ── The key: where it is found, and what happens without one ─────────────

    /// <summary>
    ///     An element carrying an id is naming a row that exists, so that is the match.
    /// </summary>
    [Fact]
    public void AnElementWithAnId_IsMatchedByIt()
    {
        var result = RunGenerator(Source("[MapTo<TestApp.Entities.Order>]"));

        var generated = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        generated.Should().Contain("d => d.Id");
        generated.Should().Contain("e => e.Id");
    }

    /// <summary>
    ///     Without an id, the child's domain key is what identifies it.
    /// </summary>
    /// <remarks>
    ///     The case of an element the caller describes rather than addresses — a line named by its
    ///     product code, not by a database id it has never seen.
    /// </remarks>
    [Fact]
    public void AnElementWithoutAnId_IsMatchedByTheChildsLogicKey()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            lineDtoKey: "public string Sku { get; init; } = \"\";",
            lineEntityKey: "[global::Pragmatic.Persistence.Entity.LogicKey] public string Sku { get; set; } = \"\";"));

        HasDiagnostic(result, "PRAG0333").Should().BeFalse();

        var generated = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        generated.Should().Contain("d => d.Sku");
        generated.Should().Contain("e => e.Sku");
    }

    /// <summary>
    ///     Nothing to match by is a build error, not a quiet fall back to rebuilding the collection.
    /// </summary>
    /// <remarks>
    ///     The fall back would be exactly the data loss the strategy exists to prevent, and it would
    ///     happen on the first update in production rather than here.
    /// </remarks>
    [Fact]
    public void NoKeyOnEitherSide_IsReported()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            lineDtoKey: "public string Description2 { get; init; } = \"\";",
            lineEntityKey: "public string Description2 { get; set; } = \"\";"));

        HasDiagnostic(result, "PRAG0333").Should().BeTrue(
            "matching every element as new would remove and rebuild the children");
    }

    /// <summary>
    ///     A child keyed by something the element DTO does not carry is the same problem, said better.
    /// </summary>
    [Fact]
    public void KeyOnTheEntityButNotOnTheDto_IsReported()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            lineDtoKey: "public string Description2 { get; init; } = \"\";",
            lineEntityKey: "[global::Pragmatic.Persistence.Entity.LogicKey] public string Sku { get; set; } = \"\";"));

        var diagnostic = GetDiagnosticsById(result, "PRAG0333").FirstOrDefault();
        diagnostic.Should().NotBeNull();
        diagnostic!.GetMessage().Should().Contain("Sku",
            "naming the key the child is identified by is what makes the message actionable");
    }

    /// <summary>
    ///     Replace matches nothing, so a missing key is not a problem for it.
    /// </summary>
    [Fact]
    public void Replace_NeedsNoKey_AndIsNotReported()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            "[CollectionStrategy(CollectionStrategy.Replace)]",
            lineDtoKey: "public string Description2 { get; init; } = \"\";",
            lineEntityKey: "public string Description2 { get; set; } = \"\";"));

        HasDiagnostic(result, "PRAG0333").Should().BeFalse(
            "rebuilding from scratch matches nothing, so there is nothing to match by");
    }

    /// <summary>
    ///     Ignore writes nothing, so it needs no key either.
    /// </summary>
    [Fact]
    public void Ignore_NeedsNoKey_AndIsNotReported()
    {
        var result = RunGenerator(Source(
            "[MapTo<TestApp.Entities.Order>]",
            "[CollectionStrategy(CollectionStrategy.Ignore)]",
            lineDtoKey: "public string Description2 { get; init; } = \"\";",
            lineEntityKey: "public string Description2 { get; set; } = \"\";"));

        HasDiagnostic(result, "PRAG0333").Should().BeFalse();
    }

    /// <summary>
    ///     A DTO that reads and writes is checked on what it writes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every case above declares <c>[MapTo]</c> alone, and for such a DTO the read and write
    ///         property lists are the same object — so a check that iterated the read list appeared to
    ///         work. Adding <c>[MapFrom]</c> separates them: the merge keeps the read list in
    ///         <c>Properties</c> while <c>ApplyTo</c> is rendered from <c>EffectiveWriteProperties</c>.
    ///     </para>
    ///     <para>
    ///         With the check on the wrong list this compiled clean and emitted the constant selector
    ///         <c>d =&gt; 0</c>, and the first update carrying two children threw
    ///         <c>DuplicateMappingKeyException</c> at runtime. Found on
    ///         <c>examples/conformance</c>, where the DTOs are bidirectional because that is the
    ///         ordinary shape.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ABidirectionalDto_IsCheckedOnItsWriteModel()
    {
        var result = RunGenerator(Source(
            "[MapFrom<TestApp.Entities.Order>]\n    [MapTo<TestApp.Entities.Order>]",
            lineDtoKey: "public string Description2 { get; init; } = \"\";",
            lineEntityKey: "public string Description2 { get; set; } = \"\";"));

        HasDiagnostic(result, "PRAG0333").Should().BeTrue(
            "reading as well as writing does not make the write side unverifiable");
    }

    // ── Creating is not updating ─────────────────────────────────────────────

    /// <summary>
    ///     <c>ToEntity</c> builds a new aggregate, so its collection is built whole — there is nothing
    ///     to merge against.
    /// </summary>
    [Fact]
    public void ToEntity_StillBuildsTheCollectionWhole()
    {
        var result = RunGenerator(Source("[MapTo<TestApp.Entities.Order>]"));

        var generated = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        generated.Should().Contain("Select(x => x.ToEntity())",
            "creating an aggregate constructs its children; only an update has to preserve them");
    }
}
