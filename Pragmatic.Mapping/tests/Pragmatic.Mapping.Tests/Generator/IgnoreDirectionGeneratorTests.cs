using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Which way a <c>[MapIgnore]</c> points.
/// </summary>
/// <remarks>
///     <para>
///         <c>[MapIgnore]</c> was all-or-nothing, so a property read and never written cost <b>two
///         DTOs for one shape</b> — two places to keep in step, and a second type in the published
///         contract.
///     </para>
///     <para>
///         ⚠️ The target here is deliberately <b>settable</b>. A computed property is excluded from
///         the write anyway — there is nothing to assign to, and PRAG0336 reports it — so a test built
///         on one would pass whether or not the attribute did anything. That is the shape the
///         conformance case first had, and it is why it is measured here instead.
///     </para>
/// </remarks>
public class IgnoreDirectionGeneratorTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp;

        public class Order
        {
            public string Reference { get; set; } = "";
            public string Channel { get; set; } = "";
        }

        [MapFrom<Order>]
        [MapTo<Order>]
        public partial class OrderDto
        {
            public string Reference { get; set; } = "";

            [MapIgnore(MappingDirection.ToEntity)]
            public string Channel { get; set; } = "";
        }
        """;

    /// <summary>
    ///     Ignored one way only: read, and not written.
    /// </summary>
    [Fact]
    public void IgnoredForTheWrite_IsStillRead()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        generated!.Should().Contain("Channel = entity.Channel",
            "the read side is untouched: the ignore names the write");
        generated.Should().NotContain("Channel = this.Channel",
            "and the write side leaves it alone");
    }

    /// <summary>
    ///     The control: without a direction, <c>[MapIgnore]</c> still means both.
    /// </summary>
    /// <remarks>
    ///     What every existing use of the attribute relies on. Without this case beside the other, an
    ///     implementation that only ever ignored the write would pass.
    /// </remarks>
    [Fact]
    public void WithoutADirection_ItIsIgnoredBothWays()
    {
        var result = RunGenerator(Source.Replace(
            "[MapIgnore(MappingDirection.ToEntity)]", "[MapIgnore]"));

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        generated!.Should().NotContain("Channel = entity.Channel", "not read");
        generated.Should().NotContain("Channel = ctx.Channel", "nor projected");
        generated.Should().NotContain("Channel = this.Channel", "nor written");
    }

    /// <summary>
    ///     And the other way round: written, and not read.
    /// </summary>
    [Fact]
    public void IgnoredForTheRead_IsStillWritten()
    {
        var result = RunGenerator(Source.Replace(
            "[MapIgnore(MappingDirection.ToEntity)]", "[MapIgnore(MappingDirection.FromEntity)]"));

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        generated!.Should().NotContain("Channel = entity.Channel");
        generated.Should().Contain("Channel = this.Channel",
            "a write-only property is what a caller may set and must not see back");
    }
}
