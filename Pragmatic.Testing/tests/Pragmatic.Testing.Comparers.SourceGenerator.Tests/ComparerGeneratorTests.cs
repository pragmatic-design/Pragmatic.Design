using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.Comparers.SourceGenerator.Tests;

/// <summary>
///     Verifies the comparer generator, and — the part that matters for a generator — that what it
///     emits compiles against the real assertion runtime.
/// </summary>
public class ComparerGeneratorTests
{
    private const string Source = """
        using Pragmatic.Testing.Assertions;

        [assembly: GenerateComparer<App.Order>]
        [assembly: GenerateComparer<App.Plain>]

        namespace App
        {
            public sealed record Order(string Customer, decimal Total)
            {
                public System.Collections.Generic.IReadOnlyList<string> Lines { get; init; } = [];
            }

            // No value equality: Equals would compare references and report a difference that is
            // not there. This is the case the generator exists for.
            public sealed class Plain
            {
                public string Name { get; set; } = "";
                public int Count { get; set; }
            }
        }
        """;

    private static (CSharpCompilation Compilation, GeneratorDriverRunResult Result) Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
            // The real runtime: AssertionFailure, AndConstraint, ObjectAssertions.
            MetadataReference.CreateFromFile(typeof(AssertionFailure).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create("ComparerGenTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComparerGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        return ((CSharpCompilation)updated, driver.GetRunResult());
    }

    private static string Generated(GeneratorDriverRunResult result, string fileNamePart) =>
        result.GeneratedTrees.FirstOrDefault(t => t.FilePath.Contains(fileNamePart))?.GetText().ToString() ?? "";

    [Fact]
    public void GeneratedCode_Compiles()
    {
        var (compilation, _) = Run(Source);

        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .Should().BeEmpty();
    }

    [Fact]
    public void Comparer_IsNamedAfterTheType()
    {
        var (_, result) = Run(Source);

        Generated(result, "Comparer.OrderComparer").Should().Contain("public static class OrderComparer");
        Generated(result, "Comparer.PlainComparer").Should().Contain("public static class PlainComparer");
    }

    [Fact]
    public void EveryReadableProperty_IsCompared()
    {
        var source = Generated(Run(Source).Result, "Comparer.OrderComparer");

        source.Should().Contain("actual.Customer").And.Contain("actual.Total").And.Contain("actual.Lines");
    }

    /// <summary>
    ///     A record's compiler-generated <c>EqualityContract</c> is not something a test compares,
    ///     and comparing it would make two records of different types differ on a member nobody wrote.
    /// </summary>
    [Fact]
    public void EqualityContract_IsNotCompared()
    {
        Generated(Run(Source).Result, "Comparer.OrderComparer").Should().NotContain("EqualityContract");
    }

    /// <summary>
    ///     A sequence member compares element by element. Reference equality on two lists holding
    ///     the same values would report a difference that is not there — which is the whole failure
    ///     mode this generator was built to remove.
    /// </summary>
    [Fact]
    public void SequenceMember_ComparesItsElements()
    {
        var source = Generated(Run(Source).Result, "Comparer.OrderComparer");

        source.Should().Contain("SequenceEqual(Elements(actual.Lines), Elements(expected.Lines))");
    }

    /// <summary>
    ///     A string is an <c>IEnumerable&lt;char&gt;</c> and is deliberately not treated as a
    ///     sequence: "element 3 differs" is not what the reader of a failing string comparison wants.
    /// </summary>
    [Fact]
    public void StringMember_IsComparedAsAValue()
    {
        var source = Generated(Run(Source).Result, "Comparer.OrderComparer");

        source.Should().Contain("Equals(actual.Customer, expected.Customer)")
            .And.NotContain("Elements(actual.Customer)");
    }

    [Fact]
    public void FailureNamesTheMember_AndShowsBothValues()
    {
        var source = Generated(Run(Source).Result, "Comparer.OrderComparer");

        source.Should().Contain("Total differs: found {mine}, expected {theirs}");
    }

    [Fact]
    public void TypeWithoutComparableMembers_ReportsPRAG2360()
    {
        const string source = """
            using Pragmatic.Testing.Assertions;

            [assembly: GenerateComparer<App.Empty>]

            namespace App { public sealed class Empty { } }
            """;

        var (_, result) = Run(source);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG2360");
        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateDeclaration_ReportsPRAG2361AndEmitsOnce()
    {
        const string source = """
            using Pragmatic.Testing.Assertions;

            [assembly: GenerateComparer<App.Thing>]
            [assembly: GenerateComparer<App.Thing>]

            namespace App { public sealed class Thing { public int Value { get; set; } } }
            """;

        var (_, result) = Run(source);

        result.Diagnostics.Select(d => d.Id).Should().Contain("PRAG2361");
        result.GeneratedTrees.Should().HaveCount(1);
    }

    /// <summary>
    ///     Inherited members are compared too, and the derived declaration wins when both declare
    ///     the same name.
    /// </summary>
    [Fact]
    public void InheritedMembers_AreCompared()
    {
        const string source = """
            using Pragmatic.Testing.Assertions;

            [assembly: GenerateComparer<App.Derived>]

            namespace App
            {
                public abstract class Base { public string Code { get; set; } = ""; }
                public sealed class Derived : Base { public int Amount { get; set; } }
            }
            """;

        var generated = Generated(Run(source).Result, "Comparer.DerivedComparer");

        generated.Should().Contain("actual.Amount").And.Contain("actual.Code");
    }
}
