using Microsoft.CodeAnalysis.Testing;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeOffsetComparisonAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="DateTimeOffsetComparisonAnalyzer"/> (PRAG0903).
/// </summary>
public class DateTimeOffsetComparisonAnalyzerTests
{
    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic(DiagnosticDescriptors.AvoidDateTimeOffsetDirectComparison);

    // =========================================================================
    // Should report PRAG0903 — direct relational comparison on DateTimeOffset
    // =========================================================================

    [Fact]
    public async Task LessThan_DateTimeOffset_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a {|#0:<|} b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("<"));
    }

    [Fact]
    public async Task GreaterThan_DateTimeOffset_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a {|#0:>|} b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments(">"));
    }

    [Fact]
    public async Task LessThanOrEqual_DateTimeOffset_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a {|#0:<=|} b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("<="));
    }

    [Fact]
    public async Task GreaterThanOrEqual_DateTimeOffset_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a {|#0:>=|} b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments(">="));
    }

    [Fact]
    public async Task LessThan_InIfStatement_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method(DateTimeOffset start, DateTimeOffset end)
    {
        if (start {|#0:<|} end)
        {
            Console.WriteLine(""start is before end"");
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0).WithArguments("<"));
    }

    // =========================================================================
    // Should NOT report PRAG0903 — explicit UTC conversion used
    // =========================================================================

    [Fact]
    public async Task LessThan_WithUtcDateTime_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a.UtcDateTime < b.UtcDateTime;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GreaterThan_WithToUniversalTime_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a.ToUniversalTime() > b.ToUniversalTime();
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LessThan_OnNonDateTimeOffset_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(int a, int b)
    {
        return a < b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LessThan_OnDateTime_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTime a, DateTime b)
    {
        return a < b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task Equality_DateTimeOffset_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    bool Method(DateTimeOffset a, DateTimeOffset b)
    {
        return a == b;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
