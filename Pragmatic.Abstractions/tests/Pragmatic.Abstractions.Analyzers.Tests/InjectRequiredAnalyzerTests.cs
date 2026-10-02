using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Abstractions.Analyzers.InjectRequiredAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Abstractions.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="InjectRequiredAnalyzer"/> (PRAG1452).
///     Uses an inline InjectAttribute stub so the analyzer resolves the attribute by metadata name
///     without requiring a Pragmatic.Abstractions project reference in the test compilation.
/// </summary>
public class InjectRequiredAnalyzerTests
{
    /// <summary>
    ///     Minimal stub matching the real InjectAttribute the analyzer keys off:
    ///     namespace Pragmatic.Composition.Attributes, type InjectAttribute, bool Required.
    /// </summary>
    private const string InjectStub = @"using Pragmatic.Composition.Attributes;
namespace Pragmatic.Composition.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Method, Inherited = false)]
    public sealed class InjectAttribute : System.Attribute
    {
        public bool Required { get; set; }
        public string? Key { get; set; }
    }
}
";

    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic("PRAG1452");

    // =========================================================================
    // PRAG1452 — should be reported (optional injection)
    // =========================================================================

    [Fact]
    public async Task Inject_DefaultOptional_OnProperty_ReportsWarning()
    {
        var test = InjectStub + @"

class Service
{
    [{|#0:Inject|}]
    public object? Dependency { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Dependency"));
    }

    [Fact]
    public async Task Inject_RequiredFalseExplicit_OnProperty_ReportsWarning()
    {
        var test = InjectStub + @"

class Service
{
    [{|#0:Inject(Required = false)|}]
    public object? Dependency { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Dependency"));
    }

    [Fact]
    public async Task Inject_DefaultOptional_OnMethod_ReportsWarning()
    {
        var test = InjectStub + @"

class Service
{
    [{|#0:Inject|}]
    public void Configure(object dependency) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Configure"));
    }

    [Fact]
    public async Task Inject_KeyOnlyNoRequired_ReportsWarning()
    {
        var test = InjectStub + @"

class Service
{
    [{|#0:Inject(Key = ""primary"")|}]
    public object? Dependency { get; set; }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Dependency"));
    }

    // =========================================================================
    // Required = true — NO diagnostic expected (fail-fast opt-in)
    // =========================================================================

    [Fact]
    public async Task Inject_RequiredTrue_OnProperty_NoDiagnostic()
    {
        var test = InjectStub + @"

class Service
{
    [Inject(Required = true)]
    public object Dependency { get; set; } = null!;
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Inject_RequiredTrue_OnMethod_NoDiagnostic()
    {
        var test = InjectStub + @"

class Service
{
    [Inject(Required = true, Key = ""primary"")]
    public void Configure(object dependency) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    // =========================================================================
    // No [Inject] — NO diagnostic expected
    // =========================================================================

    [Fact]
    public async Task NoInjectAttribute_NoDiagnostic()
    {
        var test = InjectStub + @"
class Service
{
    public object? Dependency { get; set; }

    public void Configure(object dependency) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
