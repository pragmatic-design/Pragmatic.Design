using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Xunit;
using ParameterSuppressor = Pragmatic.SourceGenerator.Suppressors.GeneratedParameterValidationSuppressor;

namespace Pragmatic.SourceGenerator.Analyzers.Tests.Suppressors;

/// <summary>
///     PRAGS003 suppresses CA1062 because "parameters in Pragmatic-*generated* types are validated by
///     the SG-generated DI constructor". That claim only covers code the generator emitted, so the
///     diagnostic must sit in a generated file.
/// </summary>
public class GeneratedParameterValidationSuppressorTests
{
    // The generator-emitted half carries no attribute — the user's half declares it.
    private const string GeneratedHalf = """
        public partial class CreateOrder
        {
            public void Invoke(string payload) { }
        }
        """;

    private const string HandWrittenHalf = """
        [Pragmatic.Actions.Attributes.Mutation]
        public partial class CreateOrder
        {
            public void Handle(string payload) { }
        }
        """;

    private static Task<System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> RunAsync()
        => SuppressorTestHarness.RunAsync(
            new ParameterSuppressor(),
            new MemberDiagnosticProducer("CA1062"),
            (PragmaticAttributeStubs.Path, PragmaticAttributeStubs.Source),
            (SuppressorTestHarness.GeneratedPath, GeneratedHalf),
            (SuppressorTestHarness.HandWrittenPath, HandWrittenHalf));

    // (1) Legitimate case — the generated invoker half.
    [Fact]
    public async Task Diagnostic_InGeneratedFile_IsSuppressed()
    {
        var diagnostics = await RunAsync();

        SuppressorTestHarness
            .Single(diagnostics, "CA1062", "Invoke", SuppressorTestHarness.GeneratedPath)
            .IsSuppressed.Should().BeTrue();
    }

    // (2) Hand-written case — the developer's own half of the same partial type. The justification
    // does not cover it, so CA1062 must remain visible.
    [Fact]
    public async Task Diagnostic_InHandWrittenFile_IsNotSuppressed()
    {
        var diagnostics = await RunAsync();

        SuppressorTestHarness
            .Single(diagnostics, "CA1062", "Handle", SuppressorTestHarness.HandWrittenPath)
            .IsSuppressed.Should().BeFalse();
    }
}
