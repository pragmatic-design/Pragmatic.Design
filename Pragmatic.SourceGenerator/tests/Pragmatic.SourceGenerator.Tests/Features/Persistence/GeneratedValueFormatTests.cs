using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Tests for the <see cref="GeneratedValueFormat" /> template parser.
/// </summary>
public class GeneratedValueFormatTests
{
    [Fact]
    public void Parse_FullTemplate_SplitsLiteralsAndTokens()
    {
        var segments = GeneratedValueFormat.Parse("INV-{YYYY}{MM}-{SEQ:5}");

        segments.Select(s => s.Kind).Should().Equal(
            GeneratedValueTokenKind.Literal,
            GeneratedValueTokenKind.Year4,
            GeneratedValueTokenKind.Month,
            GeneratedValueTokenKind.Literal,
            GeneratedValueTokenKind.Sequence);

        segments[0].Literal.Should().Be("INV-");
        segments[3].Literal.Should().Be("-");
        segments[4].Length.Should().Be(5);
    }

    [Fact]
    public void Parse_AllDateTokens_AreRecognised()
    {
        var segments = GeneratedValueFormat.Parse("{YYYY}{YY}{MM}{DD}");

        segments.Select(s => s.Kind).Should().Equal(
            GeneratedValueTokenKind.Year4,
            GeneratedValueTokenKind.Year2,
            GeneratedValueTokenKind.Month,
            GeneratedValueTokenKind.Day);
    }

    [Fact]
    public void Parse_WidthTokens_CarryKindAndLength()
    {
        var random = GeneratedValueFormat.Parse("{RANDOM:8}").Single();
        random.Kind.Should().Be(GeneratedValueTokenKind.Random);
        random.Length.Should().Be(8);

        var guid = GeneratedValueFormat.Parse("{GUID:12}").Single();
        guid.Kind.Should().Be(GeneratedValueTokenKind.Guid);
        guid.Length.Should().Be(12);

        var seq = GeneratedValueFormat.Parse("{SEQ:3}").Single();
        seq.Kind.Should().Be(GeneratedValueTokenKind.Sequence);
        seq.Length.Should().Be(3);
    }

    [Fact]
    public void Parse_PlainText_IsOneLiteral()
    {
        var segments = GeneratedValueFormat.Parse("PLAIN");

        segments.Should().ContainSingle();
        segments[0].Kind.Should().Be(GeneratedValueTokenKind.Literal);
        segments[0].Literal.Should().Be("PLAIN");
    }

    [Theory]
    [InlineData("{UNKNOWN}")]
    [InlineData("{SEQ:0}")]
    [InlineData("{SEQ:x}")]
    [InlineData("{SEQ}")]
    public void Parse_UnrecognisedToken_DegradesToLiteral(string format)
    {
        var segments = GeneratedValueFormat.Parse(format);

        segments.Should().ContainSingle();
        segments[0].Kind.Should().Be(GeneratedValueTokenKind.Literal);
        segments[0].Literal.Should().Be(format);
    }

    [Fact]
    public void Parse_Empty_ReturnsNoSegments()
    {
        GeneratedValueFormat.Parse(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void HasSequence_TrueOnlyWhenSeqTokenPresent()
    {
        GeneratedValueFormat.HasSequence(GeneratedValueFormat.Parse("INV-{SEQ:5}")).Should().BeTrue();
        GeneratedValueFormat.HasSequence(GeneratedValueFormat.Parse("INV-{RANDOM:5}")).Should().BeFalse();
        GeneratedValueFormat.HasSequence(GeneratedValueFormat.Parse("INV-{YYYY}")).Should().BeFalse();
    }
}
