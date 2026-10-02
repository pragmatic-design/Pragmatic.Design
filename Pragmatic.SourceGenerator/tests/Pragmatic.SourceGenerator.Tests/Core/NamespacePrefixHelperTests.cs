using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     The derived prefix ends up in the hint name of generated files. It must therefore be a function
///     of the input <em>set</em>, not of the order the upstream incremental provider happened to
///     enumerate it in.
/// </summary>
public class NamespacePrefixHelperTests
{
    [Fact]
    public void DerivePrefix_CommonPrefix_ReturnsLongestCommonSegments()
        => NamespacePrefixHelper.DerivePrefix(["Showcase.Booking.Entities", "Showcase.Booking.Services"])
            .Should().Be("Showcase.Booking");

    [Fact]
    public void DerivePrefix_SingleNamespace_ReturnsIt()
        => NamespacePrefixHelper.DerivePrefix(["Showcase.Booking.Entities"])
            .Should().Be("Showcase.Booking.Entities");

    [Fact]
    public void DerivePrefix_Empty_ReturnsEmpty()
        => NamespacePrefixHelper.DerivePrefix([]).Should().BeEmpty();

    // The defect: with no common prefix the helper returned list[0] — whichever namespace the provider
    // yielded first. Two runs over identical source could derive different prefixes, and so name the
    // generated files differently, for no semantic reason.
    [Fact]
    public void DerivePrefix_NoCommonPrefix_IsIndependentOfInputOrder()
    {
        var oneOrder = NamespacePrefixHelper.DerivePrefix(["Zeta.Api", "Alpha.Api", "Mid.Api"]);
        var otherOrder = NamespacePrefixHelper.DerivePrefix(["Mid.Api", "Zeta.Api", "Alpha.Api"]);

        oneOrder.Should().Be(otherOrder);
        oneOrder.Should().Be("Alpha.Api", "the fallback is the first namespace in ordinal order");
    }

    [Fact]
    public void DerivePrefix_DuplicatesAndBlanks_DoNotChangeTheResult()
        => NamespacePrefixHelper.DerivePrefix(["", "Showcase.Booking.A", "Showcase.Booking.A", "Showcase.Booking.B"])
            .Should().Be("Showcase.Booking");

    [Fact]
    public void ToIdentifier_TakesTheFirstSegment()
        => NamespacePrefixHelper.ToIdentifier("Showcase.Booking.Entities").Should().Be("Showcase");
}
