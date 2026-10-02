using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Analyzers.Tests;

/// <summary>
///     PRAG0210: a DataAnnotations validation attribute produces no check, and the analyzer says so.
/// </summary>
/// <remarks>
///     A consumer shipped <c>[Range]</c> and <c>[EmailAddress]</c> on a mutation and got a validator
///     whose body was <c>// Validate Amount</c> followed by a blank line. The comment is emitted per
///     property, so the output looked like a check had been generated.
/// </remarks>
public class IgnoredValidationAttributeAnalyzerTests
{
    // Enough of both namespaces for the analyzer to key on, so the test needs no real references.
    private const string Stubs = """
        namespace Pragmatic.Validation.Attributes
        {
            public abstract class ValidationAttribute : System.Attribute { }
            public sealed class RangeAttribute : ValidationAttribute
            {
                public RangeAttribute(double min, double max) { }
            }
        }
        namespace System.ComponentModel.DataAnnotations
        {
            public abstract class ValidationAttribute : System.Attribute { }
            public sealed class RangeAttribute : ValidationAttribute
            {
                public RangeAttribute(double min, double max) { }
            }
            public sealed class EmailAddressAttribute : ValidationAttribute { }
        }
        namespace App.Rules
        {
            // A team's own rule, written for MVC. It is not in the DataAnnotations namespace, and the
            // Pragmatic generator emits nothing for it either.
            public sealed class FiscalCodeAttribute : System.ComponentModel.DataAnnotations.ValidationAttribute { }
        }
        """;

    private static async Task<string[]> RunAsync(string appSource)
    {
        var tree = CSharpSyntaxTree.ParseText(Stubs + "\n" + appSource, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };

        var compilation = CSharpCompilation.Create(
            "IgnoredValidationTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new IgnoredValidationAttributeAnalyzer()));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(d => d.Id == "PRAG0210").Select(d => d.GetMessage()).ToArray();
    }

    [Fact]
    public async Task DataAnnotationsRange_IsReportedWithTheEquivalent()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class CreateAssignment
                {
                    [System.ComponentModel.DataAnnotations.Range(0.01, 1000000.0)]
                    public decimal Amount { get; set; }
                }
            }
            """);

        messages.Should().HaveCount(1);
        messages[0].Should().Contain("RangeAttribute").And.Contain("Pragmatic.Validation.Attributes");
    }

    [Fact]
    public async Task DataAnnotationsEmailAddress_IsReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class CreateSupplier
                {
                    [System.ComponentModel.DataAnnotations.EmailAddress]
                    public string ContactEmail { get; set; } = "";
                }
            }
            """);

        messages.Should().HaveCount(1);
        messages[0].Should().Contain("[Email]");
    }

    /// <summary>
    ///     A rule of one's own, deriving from the DataAnnotations base, is just as silent.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The check follows the attribute's <b>base type</b>, not its <b>namespace</b>. A namespace
    ///     check lets an attribute that derives from the DataAnnotations base and lives elsewhere pass
    ///     unnoticed — three of this framework's own attributes, the money rules, are exactly that
    ///     shape, and would be declarable on a Pragmatic operation while validating nothing.
    /// </remarks>
    [Fact]
    public async Task ARuleDerivingFromTheDataAnnotationsBase_IsReportedWhereverItLives()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class CreateEmployee
                {
                    [App.Rules.FiscalCode]
                    public string FiscalCode { get; set; } = "";
                }
            }
            """);

        messages.Should().HaveCount(1);
        messages[0].Should().Contain("FiscalCodeAttribute");
    }

    [Fact]
    public async Task PragmaticAttribute_IsNotReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class CreateAssignment
                {
                    [Pragmatic.Validation.Attributes.Range(0.01, 1000000.0)]
                    public decimal Amount { get; set; }
                }
            }
            """);

        messages.Should().BeEmpty("the generator acts on this one — reporting it would be noise");
    }
}
