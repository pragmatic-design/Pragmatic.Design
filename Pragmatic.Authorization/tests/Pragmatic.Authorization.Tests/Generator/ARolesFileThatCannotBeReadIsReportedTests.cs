using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     <c>roles.pragmatic.json</c> that cannot be read — not JSON, a property the file format does not have, a
///     value of the wrong kind, an inherited role that is not there, roles that inherit each other, a second
///     file — is reported, with the file's path. The roles it declares would otherwise not exist at runtime,
///     and nothing would say so.
/// </summary>
/// <remarks>
///     The transform caught <c>JsonException</c> and returned <c>null</c>; a value of the wrong kind
///     threw instead, and took the whole generator's output with it; a misspelt property (<c>permission</c>)
///     was ignored, and the role granted nothing.
/// </remarks>
public class ARolesFileThatCannotBeReadIsReportedTests : AuthorizationGeneratorTestBase
{
    private const string Path = "/app/roles.pragmatic.json";

    private static SourceGenRunResult Run(params (string Path, string Content)[] files)
        => GeneratorTestHelper.RunGeneratorWithFiles<PragmaticSourceGenerator>(
            "namespace TestApp;", files, GetAuthorizationReferences());

    private static IEnumerable<string> Messages(SourceGenRunResult result, string id)
        => result.RunResult.Diagnostics.Where(d => d.Id == id).Select(d => d.GetMessage());

    [Fact]
    public void AFileThatIsNotJson_IsReported_WithItsPathAndLine()
    {
        var result = Run((Path, """
            {
              "roles": {
                "clerk": { "permissions": ["billing.read"] }
                "desk": { "permissions": [] }
              }
            }
            """));

        var reported = result.RunResult.Diagnostics.Where(d => d.Id == "PRAG1010").ToList();
        reported.Should().HaveCount(1);
        reported[0].GetMessage().Should().Contain(Path);
        reported[0].Location.GetLineSpan().Path.Should().Be(Path);
        reported[0].Location.GetLineSpan().StartLinePosition.Line.Should().Be(3, "the error is on the fourth line");
        GetGeneratedSource(result, "RoleSeeding").Should().BeNull("a file that does not parse seeds nothing");
    }

    /// <summary>The control: a valid file reports nothing and seeds as before.</summary>
    [Fact]
    public void AValidFile_ReportsNothing_AndSeeds()
    {
        var result = Run((Path, """
            {
              "roles": {
                "clerk": { "description": "Handles invoices", "permissions": ["billing.read"] },
                "senior-clerk": { "permissions": ["billing.refund"], "inherits": ["clerk"] }
              },
              "groups": {
                "desk": { "description": "The front desk", "roles": ["clerk"] }
              }
            }
            """));

        GetGeneratorDiagnostics(result).Should().BeEmpty();
        var seeding = GetGeneratedSource(result, "RoleSeeding");
        seeding.Should().NotBeNull()
            .And.Contain("builder.MapRole(\"clerk\"")
            .And.Contain("\"billing.refund\", \"billing.read\"")
            .And.Contain("builder.MapGroup(\"desk\"");
    }

    [Theory]
    [InlineData("""{ "role": { "clerk": { "permissions": [] } } }""", "role")]
    [InlineData("""{ "roles": { "clerk": { "permission": ["billing.read"] } } }""", "permission")]
    [InlineData("""{ "groups": { "desk": { "role": ["clerk"] } } }""", "role")]
    public void APropertyTheFormatDoesNotHave_IsReported(string json, string property)
    {
        var result = Run((Path, json));

        Messages(result, "PRAG1011").Should().Contain(m => m.Contains(Path) && m.Contains($"'{property}'"));
    }

    [Theory]
    [InlineData("""{ "roles": [] }""")]
    [InlineData("""{ "roles": { "clerk": "billing.read" } }""")]
    [InlineData("""{ "roles": { "clerk": { "permissions": "billing.read" } } }""")]
    [InlineData("""{ "roles": { "clerk": { "permissions": [5] } } }""")]
    [InlineData("""{ "roles": { "clerk": { "description": 5 } } }""")]
    [InlineData("""{ "groups": { "desk": { "roles": [null] } } }""")]
    public void AValueOfTheWrongKind_IsReported_AndTheGeneratorRunsOn(string json)
    {
        var result = Run((Path, json));

        Messages(result, "PRAG1011").Should().Contain(m => m.Contains(Path));
        foreach (var run in result.RunResult.Results)
            run.Exception.Should().BeNull("a value of the wrong kind used to throw, and take every generated file with it");
        result.RunResult.Diagnostics.Should().NotContain(d => d.Id == "PRAG9000");
    }

    [Fact]
    public void AnInheritedRoleThatIsNotThere_IsReported()
    {
        var result = Run((Path, """{ "roles": { "clerk": { "permissions": [], "inherits": ["ghost"] } } }"""));

        Messages(result, "PRAG1011").Should().Contain(m => m.Contains("'ghost'") && m.Contains("'clerk'"));
    }

    [Fact]
    public void RolesThatInheritEachOther_AreReported()
    {
        var result = Run((Path, """
            { "roles": {
                "a": { "permissions": [], "inherits": ["b"] },
                "b": { "permissions": [], "inherits": ["a"] } } }
            """));

        Messages(result, "PRAG1011").Should().Contain(m => m.Contains("'a'") && m.Contains("'b'"));
    }

    [Fact]
    public void ASecondFile_IsReported_NotReadInSilence()
    {
        var result = Run(
            (Path, """{ "roles": { "clerk": { "permissions": [] } } }"""),
            ("/app/config/roles.pragmatic.json", """{ "roles": { "desk": { "permissions": [] } } }"""));

        Messages(result, "PRAG1012").Should().Contain(m => m.Contains("/app/config/roles.pragmatic.json"));
    }
}
