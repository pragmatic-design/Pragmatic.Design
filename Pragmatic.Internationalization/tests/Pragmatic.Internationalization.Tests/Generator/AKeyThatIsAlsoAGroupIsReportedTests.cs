using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     A dotted key becomes one nested class per segment and a member for the last one, so a key that
///     is also the prefix of another key asks for a member and a class of the same name in the same
///     scope — CS0102, on generated code the author cannot edit.
/// </summary>
/// <remarks>
///     The pair that made this reachable is the framework's own error convention: the resolver looked
///     up <c>{key}</c> for the detail and <c>{key}.title</c> for the title, which is exactly a key
///     that is the prefix of another. The convention now asks for <c>.detail</c> and <c>.title</c>,
///     neither a prefix of the other; the collision stays possible for any other pair, and is
///     reported rather than left to a compiler error that names no translation file.
/// </remarks>
public class AKeyThatIsAlsoAGroupIsReportedTests : I18NGeneratorTestBase
{
    private const string CollidingPair =
        """{"error.nothing.to.correct":"Nothing to correct","error.nothing.to.correct.title":"Nothing To Correct"}""";

    [Fact]
    public void Generate_KeyThatIsAlsoAGroup_IsReported()
    {
        var result = RunGeneratorWithJson(("translations/en.json", CollidingPair));

        HasDiagnostic(result, "PRAG1805").Should().BeTrue(
            "a key that is also a group breaks the build in code the author cannot edit; got: ["
            + string.Join(", ", result.RunResult.Diagnostics.Select(d => d.Id)) + "]");

        result.RunResult.Diagnostics
            .First(d => d.Id == "PRAG1805")
            .GetMessage()
            .Should().Contain("error.nothing.to.correct", "the report must name the key that collides");
    }

    [Fact]
    public void Generate_KeyThatIsAlsoAGroup_DoesNotAlsoEmitTheCollidingMember()
    {
        var result = RunGeneratorWithJson(("translations/en.json", CollidingPair));

        CompilationErrorIds(result).Should().NotContain("CS0102",
            "the report replaces the compiler error rather than accompanying it");
    }

    /// <summary>
    ///     The shape the convention now asks for: neither key is a prefix of the other, both are
    ///     generated, nothing is reported.
    /// </summary>
    [Fact]
    public void Generate_DetailAndTitlePair_GeneratesBothConstants()
    {
        var result = RunGeneratorWithJson(
            ("translations/en.json",
                """{"error.nothing.to.correct.detail":"Nothing to correct","error.nothing.to.correct.title":"Nothing To Correct"}"""));

        HasDiagnostic(result, "PRAG1805").Should().BeFalse("neither key is a prefix of the other");
        CompilationErrorIds(result).Should().NotContain("CS0102");

        var source = GetGeneratedSource(result, "T.g.cs");
        source.Should().NotBeNull();
        source!.Should().Contain("error.nothing.to.correct.detail");
        source.Should().Contain("error.nothing.to.correct.title");
    }

    /// <summary>
    ///     The control that matters most: a file with no prefix pair generates exactly what it would
    ///     without this check. Otherwise the check rewrites the API of every application that never
    ///     had the problem.
    /// </summary>
    [Fact]
    public void Generate_NoPrefixPair_IsUntouched()
    {
        var result = RunGeneratorWithJson(
            ("translations/en.json", """{"user.name":"Name","user.email":"Email"}"""));

        HasDiagnostic(result, "PRAG1805").Should().BeFalse();

        var source = GetGeneratedSource(result, "T.g.cs");
        source.Should().NotBeNull();
        source!.Should().Contain("user.name");
        source.Should().Contain("user.email");
    }

    private static List<string> CompilationErrorIds(SourceGenRunResult result)
        => result.OutputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .ToList();
}
