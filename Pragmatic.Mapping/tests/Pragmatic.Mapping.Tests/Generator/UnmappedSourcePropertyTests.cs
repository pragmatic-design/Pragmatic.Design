using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     PRAG0325, the reverse coverage: what the source has and the DTO silently drops.
/// </summary>
/// <remarks>
///     <para>
///         The count sees the members the entity will have, not only those declared in source. On an
///         entity written the way this framework recommends — traits, relations, foreign keys all
///         generated — the declared ones are a handful out of many: on the conformance <c>Order</c>,
///         one declared and seven generated. Like the path resolver, the coverage count asks
///         <c>TraitPropertyResolver</c>; counting only declared members would leave it nothing to
///         say about the very properties a DTO is most likely to forget.
///     </para>
///     <para>
///         <c>Hidden</c>, so <c>HasDiagnostic</c> is what measures it: nothing else shows it.
///     </para>
/// </remarks>
public class UnmappedSourcePropertyTests : MappingGeneratorTestBase
{
    /// <param name="dtoMembers">What the DTO declares beside <c>Reference</c>.</param>
    private static string Source(string dtoMembers = "") => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp
        {
            [Entity]
            public partial class OrderLine
            {
                public string Product { get; set; } = "";
            }

            // The navigation is not declared here: the relation generates it, so during this pass
            // it is not a symbol on Order.
            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
            public partial class Order
            {
                public string Reference { get; set; } = "";
            }

            [MapFrom<OrderLine>]
            public partial record OrderLineDto
            {
                public string Product { get; init; } = "";
            }

            [MapFrom<Order>]
            public partial record OrderDto
            {
                public string Reference { get; init; } = "";
                {{dtoMembers}}
            }
        }
        """;

    private static IEnumerable<string> Unmapped(string source)
        => GetDiagnosticsById(RunGenerator(source), "PRAG0325").Select(d => d.GetMessage());

    /// <summary>
    ///     ⚠️ The case that said nothing: the navigation the relation generates, dropped by the DTO.
    /// </summary>
    [Fact]
    public void AGeneratedNavigationTheDtoDrops_IsReported()
    {
        Unmapped(Source()).Should().Contain(m => m.Contains("'Order.Lines'"),
            "the same set the path resolver reads, so a generated member counts as dropped like a declared one");
    }

    /// <summary>
    ///     The control: mapped, the same navigation is not in the count.
    /// </summary>
    [Fact]
    public void AGeneratedNavigationTheDtoMaps_IsNotReported()
    {
        Unmapped(Source("public List<OrderLineDto> Lines { get; init; } = [];"))
            .Should().NotContain(m => m.Contains("'Order.Lines'"));
    }

    /// <summary>
    ///     And what the count always saw — a declared member — is still seen.
    /// </summary>
    [Fact]
    public void ADeclaredPropertyTheDtoDrops_IsStillReported()
    {
        var source = Source().Replace("public string Reference { get; init; } = \"\";", "");

        Unmapped(source).Should().Contain(m => m.Contains("'Order.Reference'"));
    }
}
