using Microsoft.CodeAnalysis.Testing;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeNowInTestsAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="DateTimeNowInTestsAnalyzer"/> (PRAG0904).
/// </summary>
public class DateTimeNowInTestsAnalyzerTests
{
    /// <summary>
    ///     Stub xUnit attributes for test compilation.
    ///     'using System;' is placed at the top to avoid CS1529 when concatenated with test source.
    /// </summary>
    private const string XUnitStub = @"
using System;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class FactAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TheoryAttribute : Attribute { }
}
";

    /// <summary>
    ///     Stub NUnit attributes for test compilation.
    /// </summary>
    private const string NUnitStub = @"
using System;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class TestFixtureAttribute : Attribute { }
}
";

    /// <summary>
    ///     Stub MSTest attributes for test compilation.
    /// </summary>
    private const string MSTestStub = @"
using System;

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute { }
}
";

    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic(DiagnosticDescriptors.AvoidDateTimeNowInTests);

    // =========================================================================
    // Should report PRAG0904 — DateTime.Now in test code
    // =========================================================================

    [Fact]
    public async Task DateTimeNow_InXUnitFactMethod_ReportsInfo()
    {
        var test = XUnitStub + @"
class MyTests
{
    [Xunit.Fact]
    void TestMethod()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.Now"));
    }

    [Fact]
    public async Task DateTimeUtcNow_InXUnitFactMethod_ReportsInfo()
    {
        var test = XUnitStub + @"
class MyTests
{
    [Xunit.Fact]
    void TestMethod()
    {
        var now = {|#0:DateTime.UtcNow|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.UtcNow"));
    }

    [Fact]
    public async Task DateTimeOffsetNow_InXUnitTheoryMethod_ReportsInfo()
    {
        var test = XUnitStub + @"
class MyTests
{
    [Xunit.Theory]
    void TestMethod()
    {
        var now = {|#0:DateTimeOffset.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTimeOffset.Now"));
    }

    [Fact]
    public async Task DateTimeNow_InNUnitTestMethod_ReportsInfo()
    {
        var test = NUnitStub + @"
class MyTests
{
    [NUnit.Framework.Test]
    void TestMethod()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.Now"));
    }

    [Fact]
    public async Task DateTimeNow_InNUnitTestFixtureClass_ReportsInfo()
    {
        var test = NUnitStub + @"
[NUnit.Framework.TestFixture]
class MyTests
{
    void HelperMethod()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.Now"));
    }

    [Fact]
    public async Task DateTimeNow_InMSTestTestMethod_ReportsInfo()
    {
        var test = MSTestStub + @"
class MyTests
{
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
    void TestMethod()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.Now"));
    }

    [Fact]
    public async Task DateTimeNow_InMSTestTestClass_ReportsInfo()
    {
        var test = MSTestStub + @"
[Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
class MyTests
{
    void HelperMethod()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.Now"));
    }

    [Fact]
    public async Task DateTimeNow_InClassWithFactMethod_ReportsInfo()
    {
        var test = XUnitStub + @"
class MyTests
{
    [Xunit.Fact]
    void SomeTest() { }

    void SetupHelper()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("DateTime.Now"));
    }

    // =========================================================================
    // Should NOT report PRAG0904 — not in test context
    // =========================================================================

    [Fact]
    public async Task DateTimeNow_InNonTestClass_NoDiagnostic()
    {
        var test = @"
using System;

class ProductionCode
{
    void Method()
    {
        var now = DateTime.Now;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DateTimeUtcNow_InNonTestClass_NoDiagnostic()
    {
        var test = @"
using System;

class ProductionCode
{
    void Method()
    {
        var now = DateTime.UtcNow;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task DateTimeNow_InClassWithoutTestAttributes_NoDiagnostic()
    {
        var test = XUnitStub + @"
class NotATestClass
{
    void RegularMethod()
    {
        var now = DateTime.Now;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
