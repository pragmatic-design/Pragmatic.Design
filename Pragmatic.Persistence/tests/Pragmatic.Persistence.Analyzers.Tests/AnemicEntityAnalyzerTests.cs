using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.AnemicEntityAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

/// <summary>
///     Verifies Analyzer A (PRAG0683): a behavior method on an <c>[Entity]</c> is flagged; data, computed
///     getters and object overrides are not.
/// </summary>
public class AnemicEntityAnalyzerTests
{
    private const string EntityStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }

}
";

    [Fact]
    public async Task EntityWithBehaviorMethod_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order
{
    public decimal Total { get; set; }
    public void {|#0:Cancel|}() { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            AnalyzerVerifier.Diagnostic("PRAG0683").WithLocation(0).WithArguments("Order", "Cancel"));
    }

    [Fact]
    public async Task EntityWithOnlyDataAndComputed_ReportsNothing()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order
{
    public decimal Total { get; set; }
    public bool IsFree => Total == 0m;            // computed getter — not a method
    public override string ToString() => ""Order""; // object override — not behavior
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonEntityWithMethods_ReportsNothing()
    {
        var test = EntityStub + @"
public class NotAnEntity
{
    public void DoStuff() { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
