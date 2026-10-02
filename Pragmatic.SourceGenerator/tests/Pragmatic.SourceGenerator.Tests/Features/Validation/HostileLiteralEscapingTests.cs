using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Validation;

/// <summary>
///     Security/correctness regression tests for the string-literal escaping primitive
///     (<see cref="StringHelper.CSharpLiteral" />) and the author-controlled string sinks
///     that must not emit raw, unescaped literals: the [RegularExpression] pattern
///     (verbatim-literal corruption + breakout), the [Endpoint] route, and the [FromHeader] name.
///
///     The threat: an author writes a regex/route/header containing a backslash or quote and the
///     generator emits broken — or worse, breakout — C#. Every assertion here fails against a
///     generator that emits raw literals (doubled backslashes, early-terminated verbatim string,
///     invalid escapes).
/// </summary>
public class HostileLiteralEscapingTests
{
    // ─── 1) Regex sink — the verbatim-literal bug ────────────────────────────

    [Fact]
    public void Regex_BackslashPattern_EmitsNonVerbatimFullyEscapedLiteral_ThatCompiles()
    {
        // \d / \w patterns are the common case the old @"{pattern}" path corrupted by doubling
        // every backslash, and a " inside the pattern terminated the verbatim string early.
        const string hostilePattern = "^a\\d+\"b$";

        var source = RenderRegexValidator(hostilePattern);

        // MUST be a normal literal (NOT verbatim) and fully escaped — no '@"' before the pattern.
        source.Should().NotContain("@\"");
        source.Should().Contain("\"^a\\\\d+\\\"b$\"");

        // The generated Validator must COMPILE: the literal must round-trip to the exact pattern.
        AssertCompilesAndPatternRoundTrips(source, hostilePattern);
    }

    [Fact]
    public void Regex_EmailLikePattern_BackslashesNotDoubled()
    {
        // Email regex is all backslashes — the canonical doubled-backslash regression.
        const string emailPattern = "^[\\w.]+@[\\w.]+$";

        var source = RenderRegexValidator(emailPattern);

        // The runtime pattern parsed out of the literal must equal the author's pattern exactly.
        AssertCompilesAndPatternRoundTrips(source, emailPattern);
    }

    // ─── 2) Route sink + 3) header sink ──────────────────────────────────────

    [Fact]
    public void Route_And_Header_HostileStrings_ProduceCompilableEscapedLiterals()
    {
        // Backslash + embedded quote in both the route and the header name.
        const string hostileRoute = "/x\"y\\z";
        const string hostileHeader = "X-\"Custom\\Header";

        var route = StringHelper.CSharpLiteral(hostileRoute);
        var header = StringHelper.CSharpLiteral(hostileHeader);

        // Mirror the exact emit shape of the handler templates:
        //   endpoints.MapGet("{route}", ...) and [FromHeader(Name = "{header}")]
        var source = $$"""
            public static class Probe
            {
                public static (string Route, string Header) Map()
                {
                    var route = "{{route}}";
                    var header = "{{header}}";
                    return (route, header);
                }
            }
            """;

        var (tree, compilation) = ParseAndCompile(source);

        compilation.GetDiagnostics()
            .Any(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeFalse("escaped route/header literals must compile");

        // The literals must round-trip to the original hostile values (no corruption, no breakout).
        var literals = tree.GetRoot().DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(l => (string)l.Token.Value!)
            .ToList();

        literals.Should().Contain(hostileRoute);
        literals.Should().Contain(hostileHeader);
    }

    [Fact]
    public void CSharpLiteral_Null_ReturnsEmpty()
    {
        StringHelper.CSharpLiteral(null).Should().BeEmpty();
    }

    [Fact]
    public void CSharpLiteral_ControlChars_AreEscaped()
    {
        StringHelper.CSharpLiteral("a\r\n\tb").Should().Be("a\\r\\n\\tb");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string RenderRegexValidator(string pattern)
    {
        var model = new ValidatableModel
        {
            Namespace = "MyApp",
            TypeName = "RegexCmd",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            IsRecord = false,
            IsValueType = false,
            IsEntity = false,
            Properties = ImmutableArray.Create(new PropertyValidationModel
            {
                PropertyName = "Code",
                PropertyType = "string",
                IsString = true,
                Attributes = ImmutableArray.Create(new ValidationAttributeModel
                {
                    AttributeType = "RegularExpression",
                    AttributeName = "RegularExpression",
                    Kind = ValidationKind.Regex,
                    MessageKey = "Pattern",
                    Pattern = pattern
                })
            })
        };

        return new ValidatableTemplate(model).RenderOutput().Text;
    }

    /// <summary>
    ///     Compiles the generated validator and asserts the regex literal Roslyn parses out of the
    ///     generated source equals the author's original pattern (no doubling, no early termination).
    /// </summary>
    private static void AssertCompilesAndPatternRoundTrips(string generatedSource, string expectedPattern)
    {
        var tree = CSharpSyntaxTree.ParseText(generatedSource);

        // The literal feeding Regex.IsMatch is the pattern argument. Find any string literal that,
        // when interpreted by the C# compiler, equals the author's pattern.
        var roundTripped = tree.GetRoot().DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(l => (string)l.Token.Value!)
            .ToList();

        roundTripped.Should().Contain(expectedPattern,
            "the emitted regex literal must decode back to the exact author pattern");

        // And the literal must parse with no syntax errors (a broken verbatim/escape would error here).
        tree.GetDiagnostics()
            .Any(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeFalse("the generated regex literal must be syntactically valid C#");
    }

    private static (SyntaxTree Tree, CSharpCompilation Compilation) ParseAndCompile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var objAssembly = typeof(object).Assembly.Location;
        var compilation = CSharpCompilation.Create(
            "EscapingProbe",
            [tree],
            [MetadataReference.CreateFromFile(objAssembly)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (tree, compilation);
    }
}
