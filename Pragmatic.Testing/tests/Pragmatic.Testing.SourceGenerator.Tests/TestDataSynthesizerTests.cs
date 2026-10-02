using System.IO;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.SourceGenerator.Synthesis;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies #7 phase-2 primitive data synthesis: each field type yields a valid C# value expression for a
///     CRUD create body.
/// </summary>
public class TestDataSynthesizerTests
{
    private const string Source = """
        namespace App
        {
            public enum Status { Pending, Active, Closed }
            public class Other { }
            public class Fields
            {
                public string Name { get; set; }
                public int Count { get; set; }
                public long Big { get; set; }
                public decimal Amount { get; set; }
                public bool Flag { get; set; }
                public System.Guid Id { get; set; }
                public System.DateTimeOffset When { get; set; }
                public Status State { get; set; }
                public int? MaybeCount { get; set; }
                public Other Nested { get; set; }
            }
        }
        """;

    private static System.Collections.Generic.Dictionary<string, string?> Synthesized()
    {
        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };
        var compilation = CSharpCompilation.Create("SynthTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var fields = compilation.GetTypeByMetadataName("App.Fields")!;
        return fields.GetMembers().OfType<IPropertySymbol>()
            .ToDictionary(p => p.Name, p => TestDataSynthesizer.Synthesize(p.Type));
    }

    [Fact]
    public void Primitives_ProduceValidExpressions()
    {
        var values = Synthesized();

        values["Name"].Should().Be("\"test-\" + global::System.Guid.NewGuid()");
        values["Count"].Should().Be("1");
        values["Big"].Should().Be("1L");
        values["Amount"].Should().Be("1.0m");
        values["Flag"].Should().Be("true");
        values["Id"].Should().Be("global::System.Guid.NewGuid()");
        values["When"].Should().Be("global::System.DateTimeOffset.UtcNow");
    }

    [Fact]
    public void Enum_TakesFirstMember()
    {
        Synthesized()["State"].Should().Be("global::App.Status.Pending");
    }

    [Fact]
    public void Nullable_UnwrapsToInner()
    {
        Synthesized()["MaybeCount"].Should().Be("1");
    }

    [Fact]
    public void ComplexType_IsNotSynthesizable()
    {
        // A nested complex type cannot be filled with a literal — null signals the caller to skip the success test.
        Synthesized()["Nested"].Should().BeNull();
    }
}
