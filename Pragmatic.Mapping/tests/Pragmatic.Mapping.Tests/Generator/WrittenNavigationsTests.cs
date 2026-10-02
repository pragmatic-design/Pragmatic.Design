using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     <c>WrittenNavigations</c>: the navigations the write goes into.
/// </summary>
/// <remarks>
///     <para>
///         Twin of <c>RequiredNavigations</c>, and distinct from it because they answer two different
///         questions: what must be loaded to <em>build</em> the DTO, and what must be loaded before
///         <em>writing</em> it. On a DTO whose two shapes diverge the two sets do not coincide.
///     </para>
///     <para>
///         The test that matters is the first: if the two lists were always equal, keeping two would
///         only be extra surface.
///     </para>
/// </remarks>
public class WrittenNavigationsTests : MappingGeneratorTestBase
{
    /// <summary>
    ///     ⚠️ The discriminating case: reads from one navigation and writes into another.
    /// </summary>
    /// <remarks>
    ///     <c>[MapProperty("ShippingAddress", Target = "BillingAddress")]</c> makes the two shapes
    ///     diverge. The read list names the source, the write list the target. With a single list, the
    ///     update would load <c>ShippingAddress</c> and merge into <c>BillingAddress</c>, which nobody
    ///     loaded.
    /// </remarks>
    [Fact]
    public void WhenReadAndWriteReachDifferentNavigations_TheTwoListsDiffer()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;

            namespace Test.Entities
            {
                public class Address
                {
                    public int Id { get; set; }
                    public string Street { get; set; } = "";
                }

                public class Order
                {
                    public int Id { get; set; }
                    public Address? ShippingAddress { get; set; }
                    public Address? BillingAddress { get; set; }
                }
            }

            namespace Test.Dtos
            {
                [MapFrom<Test.Entities.Address>]
                [MapTo<Test.Entities.Address>]
                public partial record AddressDto
                {
                    public int Id { get; init; }
                    public string Street { get; init; } = "";
                }

                [MapFrom<Test.Entities.Order>]
                [MapTo<Test.Entities.Order>]
                public partial record OrderDto
                {
                    public int Id { get; init; }

                    [MapProperty("ShippingAddress", Target = "BillingAddress")]
                    public AddressDto? Address { get; init; }
                }
            }
            """;

        var generated = GetGeneratedSource(RunGenerator(source), "OrderDto.Mapping");

        generated.Should().NotBeNull();
        generated!.Should().Contain(
            "public static IReadOnlyList<string> RequiredNavigations { get; } = [\"ShippingAddress\"]",
            "the read reaches the source");
        generated.Should().Contain(
            "public static IReadOnlyList<string> WrittenNavigations { get; } = [\"BillingAddress\"]",
            "the write goes into the target, which is another navigation");
    }

    /// <summary>The write list goes down into the children, prefixed by the path that reaches them.</summary>
    [Fact]
    public void ANestedWrite_IsPrefixedByThePathThatReachesIt()
    {
        var source = """
            using System.Collections.Generic;
            using Pragmatic.Mapping.Attributes;

            namespace Test.Entities
            {
                public class Allocation { public int Id { get; set; } public int Qty { get; set; } }
                public class OrderLine
                {
                    public int Id { get; set; }
                    public List<Allocation> Allocations { get; set; } = [];
                }
                public class Order
                {
                    public int Id { get; set; }
                    public List<OrderLine> Lines { get; set; } = [];
                }
            }

            namespace Test.Dtos
            {
                [MapTo<Test.Entities.Allocation>]
                public partial record AllocationDto
                {
                    public int Id { get; init; }
                    public int Qty { get; init; }
                }

                [MapTo<Test.Entities.OrderLine>]
                public partial record OrderLineDto
                {
                    public int Id { get; init; }
                    public List<AllocationDto> Allocations { get; init; } = [];
                }

                [MapTo<Test.Entities.Order>]
                public partial record OrderDto
                {
                    public int Id { get; init; }
                    public List<OrderLineDto> Lines { get; init; } = [];
                }
            }
            """;

        var generated = GetGeneratedSource(RunGenerator(source), "OrderDto.Mapping");

        generated.Should().NotBeNull();
        generated!.Should().Contain(
            "public static IReadOnlyList<string> WrittenNavigations { get; } = [\"Lines\", \"Lines.Allocations\"]",
            "two write levels, the second prefixed by the first");
    }

    /// <summary>
    ///     It is emitted on every <c>[MapTo]</c>, even when empty and even without <c>[MapFrom]</c>.
    /// </summary>
    /// <remarks>
    ///     Whoever names it — the mutation invoker, for every writable child — cannot check that it
    ///     exists: a generator does not see the symbols another one is about to write.
    /// </remarks>
    [Fact]
    public void OnAWriteOnlyDtoWithNoNavigations_TheListIsStillEmitted()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;

            namespace Test.Entities
            {
                public class Customer { public int Id { get; set; } public string Name { get; set; } = ""; }
            }

            namespace Test.Dtos
            {
                [MapTo<Test.Entities.Customer>]
                public partial record CustomerDto
                {
                    public int Id { get; init; }
                    public string Name { get; init; } = "";
                }
            }
            """;

        var generated = GetGeneratedSource(RunGenerator(source), "CustomerDto.Mapping");

        generated.Should().NotBeNull();
        generated!.Should().Contain(
            "public static IReadOnlyList<string> WrittenNavigations { get; } = [];");
        generated.Should().NotContain("RequiredNavigations",
            "that one comes from [MapFrom], which is absent here: the two lists answer different questions");
    }
}
