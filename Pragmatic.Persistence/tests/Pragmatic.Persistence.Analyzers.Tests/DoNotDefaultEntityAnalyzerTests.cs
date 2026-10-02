using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.DoNotDefaultEntityAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

public class DoNotDefaultEntityAnalyzerTests
{
    private const string EntityStub = @"
namespace Pragmatic.Persistence.Entity
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }

}
";

    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic("PRAG0681");

    // =========================================================================
    // PRAG0681 — should be reported
    // =========================================================================

    [Fact]
    public async Task DefaultExplicit_EntityType_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    void Method()
    {
        var order = {|#0:default(Order)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Order"));
    }

    [Fact]
    public async Task DefaultLiteral_EntityType_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    void Method()
    {
        Order order = {|#0:default|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Order"));
    }

    [Fact]
    public async Task DefaultExplicit_DerivedEntity_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Fee { }

public class ServiceFee : Fee { }

class TestClass
{
    void Method()
    {
        var fee = {|#0:default(ServiceFee)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("ServiceFee"));
    }

    // =========================================================================
    // Safe patterns — NO diagnostic expected
    // =========================================================================

    [Fact]
    public async Task DefaultExplicit_NonEntity_NoDiagnostic()
    {
        var test = EntityStub + @"
public class RegularClass { }

class TestClass
{
    void Method()
    {
        var obj = default(RegularClass);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultLiteral_Int_NoDiagnostic()
    {
        var test = EntityStub + @"
class TestClass
{
    void Method()
    {
        int x = default;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultLiteral_String_NoDiagnostic()
    {
        var test = EntityStub + @"
class TestClass
{
    void Method()
    {
        string s = default;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultLiteral_EqualityComparison_NoDiagnostic()
    {
        // `entity == default` is a reference null-check, not entity creation.
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    bool Method(Order order) => order == default;
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultExplicit_InequalityComparison_NoDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    bool Method(Order order) => order != default(Order);
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultLiteral_OptionalParameter_NoDiagnostic()
    {
        // A parameter default is a signature detail, not a creation site.
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    void Method(Order order = default) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultLiteral_ReturnStatement_NoDiagnostic()
    {
        // `return default;` returns null, it does not construct an entity.
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    Order Method()
    {
        return default;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DefaultLiteral_ExpressionBodiedReturn_NoDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    Order Method() => default;
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
