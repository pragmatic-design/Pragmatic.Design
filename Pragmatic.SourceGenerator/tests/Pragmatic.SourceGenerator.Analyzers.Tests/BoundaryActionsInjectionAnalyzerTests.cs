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
///     Verifies PRAG0441: the generated boundary facade injected where no permission has been checked.
/// </summary>
/// <remarks>
///     The facade enters an internal call and authorization filters skip internal calls, so injecting
///     it on the request edge turns authorization off. Trusted callers — an action, a mutation, a
///     handler, a job — have already been authorized at their own entry point and are exempt.
/// </remarks>
public class BoundaryActionsInjectionAnalyzerTests
{
    // The markers the analyzer keys on, so the test needs no real Pragmatic references.
    private const string Stubs = """
        namespace Pragmatic.Actions.Attributes
        {
            public sealed class BoundaryActionsAttribute<TBoundary> : System.Attribute { }
            public sealed class DomainActionAttribute : System.Attribute { }
        }
        namespace Pragmatic.Messaging { public interface IMessageHandler<T> { } }
        namespace App
        {
            public sealed class BillingBoundary { }

            [Pragmatic.Actions.Attributes.BoundaryActions<BillingBoundary>]
            public interface IBillingActions { void Refund(); }

            // Not a facade: same shape, no marker. Injecting it must stay silent.
            public interface IBillingCalculator { void Compute(); }
        }
        """;

    private static async Task<string[]> RunAsync(string appSource)
    {
        var tree = CSharpSyntaxTree.ParseText(
            Stubs + "\n" + appSource, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };

        var compilation = CSharpCompilation.Create(
            "BoundaryActionsInjectionTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new BoundaryActionsInjectionAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics.Where(d => d.Id == "PRAG0441").Select(d => d.GetMessage()).ToArray();
    }

    [Fact]
    public async Task AFacadeInAHandWrittenEndpoint_IsReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class RefundEndpoint
                {
                    private IBillingActions _billing = null!;
                }
            }
            """);

        messages.Should().ContainSingle();
        messages[0].Should().Contain("IBillingActions").And.Contain("RefundEndpoint");
    }

    [Fact]
    public async Task AFacadeAsAPrimaryConstructorParameter_IsReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class RefundEndpoint(IBillingActions billing)
                {
                    public IBillingActions Billing { get; } = billing;
                }
            }
            """);

        // The parameter and the property are two declarations of the same mistake, and reporting both
        // is right: each is a place a reader would have to notice it.
        messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task AFacadeInsideAnAction_IsNotReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                public class RefundOrderAction
                {
                    private IBillingActions _billing = null!;
                }
            }
            """);

        messages.Should().BeEmpty(
            "the action's own entry point checked the permission; asking again would demand a second "
            + "grant to do half of one thing");
    }

    [Fact]
    public async Task AFacadeInsideAMessageHandler_IsNotReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class InvoicePaid { }

                public class InvoicePaidHandler(IBillingActions billing)
                    : Pragmatic.Messaging.IMessageHandler<InvoicePaid>
                {
                    private readonly IBillingActions _billing = billing;
                }
            }
            """);

        messages.Should().BeEmpty("the dispatcher already runs a handler as an internal call");
    }

    /// <summary>
    ///     The control: an ordinary interface injected in the same place says nothing.
    /// </summary>
    /// <remarks>
    ///     Without it every test above would also pass on an analyzer that reported every field of
    ///     every endpoint, which would prove nothing about the marker.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryInterface_IsNotReported()
    {
        var messages = await RunAsync("""
            namespace App
            {
                public class RefundEndpoint
                {
                    private IBillingCalculator _calculator = null!;
                }
            }
            """);

        messages.Should().BeEmpty();
    }
}
