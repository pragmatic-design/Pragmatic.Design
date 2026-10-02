using Microsoft.CodeAnalysis.Testing;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeWithoutKindAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="DateTimeWithoutKindAnalyzer"/> (PRAG0902).
/// </summary>
public class DateTimeWithoutKindAnalyzerTests
{
    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic(DiagnosticDescriptors.AvoidDateTimeWithoutKind);

    // =========================================================================
    // Should report PRAG0902 — DateTime created without DateTimeKind
    // =========================================================================

    [Fact]
    public async Task NewDateTime_YearMonthDay_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = {|#0:new DateTime(2024, 1, 15)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0));
    }

    [Fact]
    public async Task NewDateTime_YearMonthDayHourMinuteSecond_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = {|#0:new DateTime(2024, 1, 15, 10, 30, 0)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0));
    }

    [Fact]
    public async Task NewDateTime_Ticks_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = {|#0:new DateTime(638000000000000000L)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0));
    }

    [Fact]
    public async Task NewDateTime_YearMonthDayHourMinuteSecondMs_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = {|#0:new DateTime(2024, 1, 15, 10, 30, 0, 500)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectDiagnostic().WithLocation(0));
    }

    // =========================================================================
    // Should NOT report PRAG0902 — DateTime created with DateTimeKind
    // =========================================================================

    [Fact]
    public async Task NewDateTime_WithDateTimeKind_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewDateTime_TicksWithKind_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = new DateTime(638000000000000000L, DateTimeKind.Local);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewDateTime_Parameterless_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var dt = new DateTime();
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewNonDateTime_NoDiagnostic()
    {
        var test = @"
using System;

class MyDateTime
{
    public MyDateTime(int year, int month, int day) { }
}

class TestClass
{
    void Method()
    {
        var dt = new MyDateTime(2024, 1, 15);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
