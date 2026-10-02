using Pragmatic.SourceGen;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     A route segment becoming a C# parameter name.
/// </summary>
/// <remarks>
///     The trait endpoints built it as <c>segment.TrimEnd('s') + "Id"</c>. A consumer wrote
///     <c>[Resource("case-files")]</c> and the attachment endpoints came out with a parameter called
///     <c>case-fileId</c> — code that cannot compile, and which the author could only escape by
///     renaming the resource in the URL. The same line also stripped every trailing <c>s</c>.
/// </remarks>
public class RouteSegmentIdentifierTests
{
    [Theory]
    [InlineData("case-files", "caseFile")]
    [InlineData("work_items", "workItem")]
    [InlineData("reservations", "reservation")]
    [InlineData("properties", "property")]
    [InlineData("addresses", "address")]
    [InlineData("boxes", "box")]
    public void Segment_BecomesACamelCaseSingularIdentifier(string segment, string expected)
        => StringHelper.ToCamelCaseIdentifier(StringHelper.Singularize(segment)).Should().Be(expected);

    /// <summary>A word ending in a double <c>s</c> is not a plural.</summary>
    [Theory]
    [InlineData("class", "class")]
    [InlineData("address", "address")]
    public void DoubleS_IsNotStripped(string segment, string expected)
        => StringHelper.Singularize(segment).Should().Be(expected);

    /// <summary>
    ///     Whatever the segment contains, the result has to be usable as an identifier — that is the
    ///     property the old code lacked, not any particular spelling.
    /// </summary>
    [Theory]
    [InlineData("case-files")]
    [InlineData("a.b/c")]
    [InlineData("2fa-tokens")]
    [InlineData("--weird--")]
    public void Result_IsAlwaysAValidIdentifier(string segment)
    {
        var identifier = StringHelper.ToCamelCaseIdentifier(StringHelper.Singularize(segment)) + "Id";

        identifier.Should().MatchRegex("^[A-Za-z_][A-Za-z0-9_]*$");
    }

    [Fact]
    public void SingularizeAndPluralize_RoundTrip()
    {
        foreach (var word in new[] { "reservation", "property", "address", "box", "case", "dish" })
            StringHelper.Singularize(StringHelper.Pluralize(word)).Should().Be(word);
    }

    /// <summary>
    ///     The documented limit: "buses" and "cases" are the same shape and English does not say
    ///     which is which without a dictionary. The common one wins, and the other yields a readable
    ///     wrong singular rather than a wrong rule.
    /// </summary>
    [Fact]
    public void Singularize_BareSes_PrefersTheCommonShape()
    {
        StringHelper.Singularize("cases").Should().Be("case");
        StringHelper.Singularize("buses").Should().Be("buse");
    }
}
