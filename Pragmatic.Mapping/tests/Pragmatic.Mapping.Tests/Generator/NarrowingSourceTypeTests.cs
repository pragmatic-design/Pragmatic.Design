using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A DTO that declares a type the source does not fit into.
/// </summary>
/// <remarks>
///     <para>
///         The incompatible pair is refused by <c>PRAG0304</c>, and the template knows it: where the
///         refusal fires it emits <c>default!</c> with a comment, «so the build shows the clean
///         diagnostic instead of a second, cryptic <c>CS0266</c> inside a <c>.g.cs</c>». That guard must
///         come <b>before</b> the automatic-default branch, which only looks at whether the source is
///         nullable and does not compare it with the target: otherwise a <c>long?</c> source on an
///         <c>int</c> takes the automatic default, the incompatibility is never reached, and the build
///         dies with exactly the <c>CS0266</c> that branch exists to avoid.
///     </para>
///     <para>
///         ⚠️ The defect would be in the <b>order</b>, not in the diagnostic: <c>PRAG0304</c> fires
///         either way. What the author would see is two errors — one naming their property and one on a
///         line they did not write — instead of the first alone.
///     </para>
/// </remarks>
public class NarrowingSourceTypeTests : MappingGeneratorTestBase
{
    private static string Source(string sourceType, string dtoType, bool projection = false) => $$"""
        using System;
        using Pragmatic.Mapping.Attributes;

        public class Src { public {{sourceType}} Value { get; set; } }

        [MapFrom<Src>]
        {{(projection ? "[GenerateProjection]" : "")}}
        public partial class Dto { public {{dtoType}} Value { get; init; } }
        """;

    private static string Errors(SourceGenRunResult result)
        => string.Join(" | ", GetCompilationErrors(result).Select(d => d.ToString()));

    /// <summary>The refused pair does not also produce code that does not compile.</summary>
    [Theory]
    [InlineData("long", "int")]
    [InlineData("long?", "int")]
    [InlineData("decimal?", "int")]
    [InlineData("double?", "float")]
    public void ARefusedPair_DoesNotAlsoBreakTheBuild(string sourceType, string dtoType)
    {
        var result = RunGenerator(Source(sourceType, dtoType));

        HasDiagnostic(result, "PRAG0304").Should().BeTrue(
            "the pair is incompatible, and the diagnostic naming it is the right message");

        HasCompilationErrors(result).Should().BeFalse(Errors(result));
    }

    /// <summary>And the same in the projection, which is the other caller of the same branch.</summary>
    [Theory]
    [InlineData("long?", "int")]
    [InlineData("decimal?", "int")]
    public void ARefusedPair_DoesNotBreakTheProjectionEither(string sourceType, string dtoType)
    {
        var result = RunGenerator(Source(sourceType, dtoType, projection: true));

        HasDiagnostic(result, "PRAG0304").Should().BeTrue();
        HasCompilationErrors(result).Should().BeFalse(Errors(result));
    }

    /// <summary>
    ///     The control: a compatible pair stays silent.
    /// </summary>
    /// <remarks>
    ///     Without it, a refusal applied always — the opposite error — would go unnoticed:
    ///     <c>PRAG0304</c> on every property would satisfy the cases above.
    /// </remarks>
    [Theory]
    [InlineData("int", "long")]
    [InlineData("int", "int")]
    [InlineData("int?", "int")]
    [InlineData("DateTimeOffset?", "DateTimeOffset")]
    [InlineData("string", "string")]
    public void ACompatiblePair_IsNotRefused(string sourceType, string dtoType)
    {
        var result = RunGenerator(Source(sourceType, dtoType));

        HasDiagnostic(result, "PRAG0304").Should().BeFalse(
            "the source fits the target, by widening or because it is the same type");

        HasCompilationErrors(result).Should().BeFalse(Errors(result));
    }

    /// <summary>
    ///     The silent substitution has a name: <c>PRAG0341</c>.
    /// </summary>
    /// <remarks>
    ///     It is the complement of <c>PRAG0317</c>, which is an error and deliberately excludes simple
    ///     types, because there the default is synthesized instead of refused. The typical case: a
    ///     <c>DateTimeOffset</c> declared over a nullable <c>UpdatedAt</c> puts <c>0001-01-01</c> on the
    ///     wire for every row never updated — a date that reads as data.
    /// </remarks>
    [Theory]
    [InlineData("DateTimeOffset?", "DateTimeOffset")]
    [InlineData("int?", "int")]
    [InlineData("decimal?", "decimal")]
    public void ANullableSourceOnANonNullableProperty_SaysTheDefaultIsSubstituted(string sourceType, string dtoType)
    {
        var result = RunGenerator(Source(sourceType, dtoType));

        HasDiagnostic(result, "PRAG0341").Should().BeTrue(
            "the substituted default is a fact on the wire, and it must be said");

        HasCompilationErrors(result).Should().BeFalse(Errors(result));
    }

    /// <summary>The control: where nothing is substituted, nothing is said.</summary>
    /// <remarks>
    ///     A source that cannot be missing, and a target that admits null, are the two ways the question
    ///     does not arise. Without this case, a diagnostic emitted on every property would satisfy the
    ///     one above.
    /// </remarks>
    [Theory]
    [InlineData("int", "int")]
    [InlineData("DateTimeOffset", "DateTimeOffset")]
    [InlineData("int?", "int?")]
    [InlineData("DateTimeOffset?", "DateTimeOffset?")]
    public void NothingIsSubstituted_NothingIsSaid(string sourceType, string dtoType)
    {
        var result = RunGenerator(Source(sourceType, dtoType));

        HasDiagnostic(result, "PRAG0341").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse(Errors(result));
    }
}
