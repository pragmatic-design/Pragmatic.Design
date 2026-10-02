using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Xunit;
using StaticSuppressor = Pragmatic.SourceGenerator.Suppressors.PartialMethodStaticSuppressor;

namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     PRAGS002 suppresses CA1822 because "the method is in a partial class with SG-generated members
///     that may use instance data". CA1822 looks at what *this* method touches, not at its callers,
///     so the claim only holds where the other half of the method lives in generated code — a partial
///     method — or where the member itself is generated.
/// </summary>
public class PartialMethodStaticSuppressorTests
{
    private const string GeneratedHalf = """
        public partial class GetOrders
        {
            partial void OnConfigured();

            public void Prepare() { }
        }
        """;

    private const string HandWrittenHalf = """
        [Pragmatic.Actions.Attributes.Query]
        public partial class GetOrders
        {
            partial void OnConfigured() { }

            public void Compute() { }
        }
        """;

    private static Task<System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> RunAsync()
        => SuppressorTestHarness.RunAsync(
            new StaticSuppressor(),
            new MemberDiagnosticProducer("CA1822"),
            (PragmaticAttributeStubs.Path, PragmaticAttributeStubs.Source),
            (SuppressorTestHarness.GeneratedPath, GeneratedHalf),
            (SuppressorTestHarness.HandWrittenPath, HandWrittenHalf));

    // (1) Legitimate case — the implementing half of a partial method; the declaring half is generated.
    [Fact]
    public async Task PartialMethodImplementation_IsSuppressed()
    {
        var diagnostics = await RunAsync();

        SuppressorTestHarness
            .Single(diagnostics, "CA1822", "OnConfigured", SuppressorTestHarness.HandWrittenPath)
            .IsSuppressed.Should().BeTrue();
    }

    // (1b) Legitimate case — a member the generator wrote.
    [Fact]
    public async Task MethodInGeneratedFile_IsSuppressed()
    {
        var diagnostics = await RunAsync();

        SuppressorTestHarness
            .Single(diagnostics, "CA1822", "Prepare", SuppressorTestHarness.GeneratedPath)
            .IsSuppressed.Should().BeTrue();
    }

    // (2) Hand-written case — an ordinary method the developer wrote. CA1822 is a real finding here
    // (this repo even treats it as an error) and must remain visible.
    [Fact]
    public async Task OrdinaryHandWrittenMethod_IsNotSuppressed()
    {
        var diagnostics = await RunAsync();

        SuppressorTestHarness
            .Single(diagnostics, "CA1822", "Compute", SuppressorTestHarness.HandWrittenPath)
            .IsSuppressed.Should().BeFalse();
    }

    // (2b) Hand-written case — no `partial` on the type means no generated half exists.
    [Fact]
    public async Task MethodOnNonPartialType_IsNotSuppressed()
    {
        var diagnostics = await SuppressorTestHarness.RunAsync(
            new StaticSuppressor(),
            new MemberDiagnosticProducer("CA1822"),
            (PragmaticAttributeStubs.Path, PragmaticAttributeStubs.Source),
            (SuppressorTestHarness.HandWrittenPath, """
                [Pragmatic.Actions.Attributes.Query]
                public class GetCustomers
                {
                    public void Compute() { }
                }
                """));

        SuppressorTestHarness
            .Single(diagnostics, "CA1822", "Compute")
            .IsSuppressed.Should().BeFalse();
    }
}
