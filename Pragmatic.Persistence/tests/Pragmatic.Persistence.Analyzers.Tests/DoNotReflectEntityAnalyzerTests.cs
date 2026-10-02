using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.DoNotReflectEntityAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

public class DoNotReflectEntityAnalyzerTests
{
    private const string EntityStub = @"
using System;
namespace Pragmatic.Persistence.Entity
{
    [AttributeUsage(AttributeTargets.Class)]
    public class EntityAttribute : Attribute { }

}
";

    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic("PRAG0682");

    // =========================================================================
    // PRAG0682 — should be reported
    // =========================================================================

    [Fact]
    public async Task CreateInstanceGeneric_EntityType_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    void Method()
    {
        var order = {|#0:System.Activator.CreateInstance<Order>()|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Order"));
    }

    [Fact]
    public async Task CreateInstanceTypeof_EntityType_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Order { }

class TestClass
{
    void Method()
    {
        var order = {|#0:Activator.CreateInstance(typeof(Order))|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("Order"));
    }

    [Fact]
    public async Task CreateInstanceGeneric_DerivedEntity_ReportsDiagnostic()
    {
        var test = EntityStub + @"
[Pragmatic.Persistence.Entity.Entity]
public partial class Fee { }

public class ServiceFee : Fee { }

class TestClass
{
    void Method()
    {
        var fee = {|#0:Activator.CreateInstance<ServiceFee>()|};
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
    public async Task CreateInstanceGeneric_NonEntity_NoDiagnostic()
    {
        var test = EntityStub + @"
public class RegularClass { }

class TestClass
{
    void Method()
    {
        var obj = System.Activator.CreateInstance<RegularClass>();
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CreateInstanceTypeof_NonEntity_NoDiagnostic()
    {
        var test = EntityStub + @"
public class RegularClass { }

class TestClass
{
    void Method()
    {
        var obj = System.Activator.CreateInstance(typeof(RegularClass));
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CreateInstanceTypeof_DifferentNamespace_NoDiagnostic()
    {
        var test = @"
namespace SomeOther.Namespace
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class EntityAttribute : System.Attribute { }
}

[SomeOther.Namespace.Entity]
public class MyClass { }

class TestClass
{
    void Method()
    {
        var obj = System.Activator.CreateInstance(typeof(MyClass));
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
