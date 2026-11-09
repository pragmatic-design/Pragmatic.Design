using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     The generated <c>[Regex]</c> check passes an empty text, as <c>RegexAttribute.IsValid</c> does.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The generator wrote <c>Regex.IsMatch(value ?? "", pattern)</c>: it guarded <c>null</c> and matched
///         <c>""</c> against the pattern, while the attribute skips both. An optional country code sent empty was
///         refused <c>validation.regex</c> by the generated validator and accepted by the attribute, so the answer
///         depended on which of the two paths ran. Refusing an empty text is <c>[Required]</c>'s job, and the last
///         case pins that it still does.
///     </para>
///     <para>These run the generated validator.</para>
/// </remarks>
public class AnEmptyTextPassesARegexTests : ValidationGeneratorTestBase
{
    private const string CountryCode = """[Regex("^[A-Z]{2}$")]""";

    [Fact]
    public void Validate_AnEmptyText_PassesTheRegex()
    {
        IssuesFor(CountryCode, "").Should().BeEmpty("an empty text is the concern of [Required], not of the pattern");
    }

    [Fact]
    public void Validate_ATextThatDoesNotMatch_IsRefused()
    {
        IssuesFor(CountryCode, "Italy").Should().Equal("validation.regex");
    }

    [Fact]
    public void Validate_ATextThatMatches_HasNoIssue()
    {
        IssuesFor(CountryCode, "IT").Should().BeEmpty();
    }

    [Fact]
    public void Validate_AnEmptyText_WhereItIsRequired_IsRefusedAsMissing()
    {
        IssuesFor("[Required]" + CountryCode, "").Should().Equal("validation.required");
    }

    private static IReadOnlyList<string> IssuesFor(string rule, string value)
    {
        var result = RunGenerator($$"""
            using Pragmatic.Validation.Attributes;

            namespace TestNamespace
            {
                public partial record AddressRequest
                {
                    {{rule}}
                    public string Country { get; init; } = "";
                }
            }
            """);

        var assembly = EmitAndLoad(result);
        var request = Activator.CreateInstance(assembly.GetType("TestNamespace.AddressRequest")!)!;
        request.GetType().GetProperty("Country")!.SetValue(request, value);

        return ((ISyncValidator)request).Validate().Issues.Select(i => i.MessageKey).ToList();
    }
}
