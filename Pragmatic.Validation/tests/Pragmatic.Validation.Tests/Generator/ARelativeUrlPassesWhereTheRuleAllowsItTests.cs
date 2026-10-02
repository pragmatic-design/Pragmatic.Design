using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     <c>[Url(RequireAbsolute = false)]</c> accepts a relative URL in the generated validator.
/// </summary>
/// <remarks>
///     The generator emitted <c>UrlAttribute.IsValidUrl(value, schemes)</c> and never passed
///     <c>RequireAbsolute</c>, so the option was declared and never read: a relative URL was rejected on
///     the path every generated validator takes. These run the generated validator.
/// </remarks>
public class ARelativeUrlPassesWhereTheRuleAllowsItTests : ValidationGeneratorTestBase
{
    [Fact]
    public void Validate_ARelativePath_WhereTheRuleAllowsIt_HasNoIssue()
    {
        IssuesFor("[Url(RequireAbsolute = false)]", "/docs/page").Should().BeEmpty();
    }

    /// <summary>The control: the default still requires an absolute URL.</summary>
    [Fact]
    public void Validate_ARelativePath_WithTheDefault_IsRefused()
    {
        IssuesFor("[Url]", "/docs/page").Should().Equal("validation.url");
    }

    private static IReadOnlyList<string> IssuesFor(string rule, string value)
    {
        var result = RunGenerator($$"""
            using Pragmatic.Validation.Attributes;

            namespace TestNamespace
            {
                public partial record LinkRequest
                {
                    {{rule}}
                    public string Link { get; init; } = "";
                }
            }
            """);

        var assembly = EmitAndLoad(result);
        var request = Activator.CreateInstance(assembly.GetType("TestNamespace.LinkRequest")!)!;
        request.GetType().GetProperty("Link")!.SetValue(request, value);

        return ((ISyncValidator)request).Validate().Issues.Select(i => i.MessageKey).ToList();
    }
}
