using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     What pairs two enums, and the cast between an enum and its number.
/// </summary>
/// <remarks>
///     <para>
///         By name is the default and stays it: two enums reordered produce no wrong data in silence,
///         because the pairing never depended on the order. By value is for lining up with something
///         that already decided the numbers — and it is a cast, so it also removes the compile-time
///         check that <c>PRAG0328</c> performs.
///     </para>
///     <para>
///         The enum ↔ number pair is here too: it needed a <c>[MapConverter]</c> class for something
///         the language does in one token.
///     </para>
/// </remarks>
public class EnumMatchGeneratorTests : MappingGeneratorTestBase
{
    private const string TwoEnums = """
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp;

        public enum Wire { A = 1, B = 2 }
        public enum Domain { X = 1, Y = 2 }

        public class Order
        {
            public Domain Status { get; set; }
        }

        [MapFrom<Order>]
        public partial class OrderDto
        {
            public Wire Status { get; init; }
        }
        """;

    /// <summary>
    ///     ⚠️ Without the attribute, enums whose members do not line up by name are not mapped.
    /// </summary>
    /// <remarks>
    ///     The control case, and the reason by value has to be asked for: <c>A</c> and <c>X</c> share
    ///     a number and nothing else. Mapping them silently is the failure this default prevents.
    /// </remarks>
    [Fact]
    public void ByName_WhenTheMembersDoNotLineUp_ItIsNotMapped()
    {
        var result = RunGenerator(TwoEnums);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("Status = ",
            "no member of Wire exists on Domain, so there is nothing this could mean");
    }

    /// <summary>
    ///     Declared by value: a cast, whatever the members are called.
    /// </summary>
    [Fact]
    public void ByValue_IsACast()
    {
        var result = RunGenerator(TwoEnums.Replace(
            "public Wire Status { get; init; }",
            "[MapEnum(EnumMatch.ByValue)]\n    public Wire Status { get; init; }"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("(global::TestApp.Wire)",
            "by value is the cast, not a switch over names");
        generated.Should().NotContain("switch",
            "and the by-name switch is gone, not merely joined");
    }

    /// <summary>
    ///     An enum and its number, in both directions.
    /// </summary>
    /// <remarks>
    ///     Asserted by compiling as well as by reading: the cast has to be the right way round on
    ///     each side, and a text check alone cannot tell one direction from the other.
    /// </remarks>
    [Fact]
    public void AnEnumAndItsNumber_AreACastBothWays()
    {
        var source = """
            using Pragmatic.Mapping;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public enum Status { Draft = 0, Sent = 1 }

            public class Order
            {
                public Status Status { get; set; }
            }

            [MapFrom<Order>]
            [MapTo<Order>]
            public partial class OrderDto
            {
                public int Status { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("(int)", "reading the enum gives its number");
        generated.Should().Contain("(global::TestApp.Status)", "and writing takes it back");
    }
}
