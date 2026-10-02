using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.EFCore;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A path that crosses a boundary says so, instead of reading as a typo.
/// </summary>
/// <remarks>
///     <para>
///         A relation across a boundary generates no navigation, and that is deliberate: the two
///         entities live in different <c>DbContext</c>s, so there is nothing for EF to include. What
///         the author saw was <c>PRAG0302: property not found on source type</c> — true, and the
///         reading it invites is "I misspelled something". The relation is declared, the spelling is
///         right, and the same shape works when both entities sit in one boundary.
///     </para>
///     <para>
///         PRAG0334 replaces it for this case rather than joining it: two diagnostics for one absence
///         would leave the reader deciding which of them is the real one.
///     </para>
/// </remarks>
public class CrossBoundaryPathTests
{
    /// <param name="orderBoundary">The boundary the entity being mapped belongs to.</param>
    /// <param name="salesBoundaryAttributes">What <c>SalesBoundary</c> declares — a <c>[ReadAccess]</c>, or nothing.</param>
    private static string Source(string orderBoundary, string salesBoundaryAttributes = "") => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp;

        {{salesBoundaryAttributes}}
        public class SalesBoundary;
        public class BillingBoundary;

        [Entity]
        [BelongsTo<BillingBoundary>]
        public partial class Customer : IEntity
        {
            public string Name { get; set; } = "";
        }

        [Entity]
        [BelongsTo<{{orderBoundary}}>]
        [Relation.ManyToOne<Customer>]
        public partial class Order : IEntity
        {
            public string Reference { get; set; } = "";
        }

        [MapFrom<Order>]
        public partial class OrderDto
        {
            [MapProperty("Customer.Name")]
            public string CustomerName { get; init; } = "";
        }
        """;

    [Fact]
    public void APathAcrossABoundary_SaysWhyTheNavigationIsNotThere()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source("SalesBoundary"), References());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0334").Should().BeTrue(
            "the relation is declared and the navigation was deliberately not generated");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0302").Should().BeFalse(
            "one absence, one diagnostic — the reader should not have to pick which is the real one");
    }

    /// <summary>
    ///     And it names the remedy that works.
    /// </summary>
    /// <remarks>
    ///     The message was written before <c>[ReadAccess&lt;T&gt;]</c> existed, and kept sending the
    ///     author to rewrite the DTO — the other side's operations, or a remote boundary — when one
    ///     line on the boundary that reads is what makes the path resolve. The case below measures
    ///     that line; this one measures that the diagnostic points at it.
    /// </remarks>
    [Fact]
    public void APathAcrossABoundary_NamesTheRemedyThatWorks()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source("SalesBoundary"), References());

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0334").Single().GetMessage()
            .Should().Contain("[ReadAccess<", "the remedy that keeps the DTO as it is");
    }

    /// <summary>
    ///     Where the boundary declares that it reads across, the path resolves like any other.
    /// </summary>
    /// <remarks>
    ///     The half nobody measured: the file proved that <c>PRAG0334</c> fires when the read is not
    ///     declared, and nothing proved that the same path is fine when it is. The two mechanisms —
    ///     the navigation generated for a <c>[ReadAccess]</c>, and the mapping that walks it — meet
    ///     here and nowhere else in the generator tests.
    /// </remarks>
    [Fact]
    public void TheSamePathWhereTheBoundaryReadsAcross_Resolves()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source("SalesBoundary", "[ReadAccess<Customer>]"), References());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0334").Should().BeFalse(
            "the boundary reads across, so the navigation is generated and the path has a first segment");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0302").Should().BeFalse();

        GeneratorTestHelper.GetGeneratedSource(result, "OrderDto.Mapping")!
            .Should().Contain("entity.Customer", "the path resolved through the navigation instead of being dropped");
    }

    /// <remarks>
    ///     The control. Same relation, same path, both entities in one boundary: the navigation exists
    ///     and nothing is reported. Without it, the test above would pass on a generator that reported
    ///     PRAG0334 for every unresolved path.
    /// </remarks>
    [Fact]
    public void TheSamePathWithinOneBoundary_IsFine()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source("BillingBoundary"), References());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0334").Should().BeFalse();
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0302").Should().BeFalse();
    }

    /// <remarks>
    ///     And a path that names nothing at all still reads as what it is.
    /// </remarks>
    [Fact]
    public void APathThatNamesNothing_IsStillPropertyNotFound()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>("""
            using System;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public string Reference { get; set; } = "";
            }

            [MapFrom<Order>]
            public partial class OrderDto
            {
                [MapProperty("Custmoer.Name")]
                public string CustomerName { get; init; } = "";
            }
            """, References());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0302").Should().BeTrue();
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0334").Should().BeFalse();
    }

    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
        GeneratorTestHelper.FromType<Actions.Attributes.ReadAccessAttribute<object>>(),
        GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<DbContext>(),
    ];
}
