using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.Documents.Csv.Generator.Tests;

/// <summary>
/// Drives the source generator in-memory to assert its diagnostics (which cannot be observed through
/// the runtime-behaviour tests). PRAG1900 warns when a property type cannot be read back from CSV.
/// </summary>
public class CsvGeneratorDiagnosticTests
{
    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "GenDiagTest",
            [CSharpSyntaxTree.ParseText(source, path: "Test.cs")],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new CsvSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (output, diagnostics);
    }

    [Fact]
    public void UnsupportedPropertyType_ReportsPrag1900_AndGeneratesCompilingCode()
    {
        const string source = """
            using Pragmatic.Documents.Csv;
            namespace T;
            public sealed class Widget { public int X { get; set; } }
            [CsvSerializable]
            public partial class Row
            {
                public string Name { get; set; } = "";
                public Widget Gadget { get; set; } = new();
            }
            """;

        var (output, diagnostics) = Run(source);

        // The generator warns about the unsupported 'Widget' property...
        diagnostics.Should().Contain(d => d.Id == "PRAG1900" && d.Severity == DiagnosticSeverity.Warning);
        // ...and the generated code still compiles (the property is written but skipped on read).
        output.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AllSupportedTypes_ProduceNoDiagnostics()
    {
        const string source = """
            using System;
            using Pragmatic.Documents.Csv;
            namespace T;
            public enum Status { A, B }
            [CsvSerializable]
            public partial class Row
            {
                public string Name { get; set; } = "";
                public int Count { get; set; }
                public Status State { get; set; }
                public Guid Id { get; set; }
                public TimeSpan Span { get; set; }
                public DateTime When { get; set; }
            }
            """;

        var (output, diagnostics) = Run(source);

        diagnostics.Should().BeEmpty();
        output.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }
}
