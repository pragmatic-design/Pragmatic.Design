using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pragmatic.SourceGenerator.Analyzers;
using Xunit;

namespace Pragmatic.SourceGenerator.Analyzers.Tests;

/// <summary>
///     Verifies the event-cascade cycle analyzer (#6b): a handler reacting to one event that triggers an
///     operation re-raising another, looping back, is reported as PRAG0822; an acyclic graph is not.
/// </summary>
public class EventCycleAnalyzerTests
{
    // Minimal stubs for the markers the analyzer keys on, so the test needs no real Pragmatic references.
    private const string Stubs = """
        namespace Pragmatic.Authoring { public sealed class RaisesAttribute<T> : System.Attribute { } }
        namespace Pragmatic.Messaging { public interface IMessageHandler<T> { } }
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
            "EventCycleTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new EventCycleAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics.Where(d => d.Id == "PRAG0822").Select(d => d.GetMessage()).ToArray();
    }

    [Fact]
    public async Task TwoEventCycle_IsReported()
    {
        var source = """
            namespace App
            {
                public class EventA { }
                public class EventB { }

                public class OperationA { [Pragmatic.Authoring.Raises<EventA>] public void Run() { } }
                public class OperationB { [Pragmatic.Authoring.Raises<EventB>] public void Run() { } }

                // Handler for A triggers an operation that raises B; handler for B triggers one that raises A → cycle.
                public class HandlerA : Pragmatic.Messaging.IMessageHandler<EventA>
                {
                    public void Handle() { var op = new OperationB(); op.Run(); }
                }
                public class HandlerB : Pragmatic.Messaging.IMessageHandler<EventB>
                {
                    public void Handle() { var op = new OperationA(); op.Run(); }
                }
            }
            """;

        var diagnostics = await RunAsync(source);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Should().Contain("EventA").And.Contain("EventB");
    }

    [Fact]
    public async Task AcyclicGraph_IsNotReported()
    {
        var source = """
            namespace App
            {
                public class EventA { }
                public class EventB { }

                public class OperationB { [Pragmatic.Authoring.Raises<EventB>] public void Run() { } }

                // Handler for A triggers an operation raising B, but nothing raises A back → no cycle.
                public class HandlerA : Pragmatic.Messaging.IMessageHandler<EventA>
                {
                    public void Handle() { var op = new OperationB(); op.Run(); }
                }
                public class HandlerB : Pragmatic.Messaging.IMessageHandler<EventB>
                {
                    public void Handle() { }
                }
            }
            """;

        var diagnostics = await RunAsync(source);

        diagnostics.Should().BeEmpty();
    }
}
