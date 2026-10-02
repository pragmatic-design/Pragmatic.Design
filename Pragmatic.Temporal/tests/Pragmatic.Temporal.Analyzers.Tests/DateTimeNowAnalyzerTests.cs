using Microsoft.CodeAnalysis.Testing;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeNowAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="DateTimeNowAnalyzer"/> (PRAG0900 Now/UtcNow, PRAG0901 Today).
/// </summary>
public class DateTimeNowAnalyzerTests
{
    private static DiagnosticResult ExpectNow(string fullName)
        => AnalyzerVerifier.Diagnostic(DiagnosticDescriptors.AvoidDateTimeNow).WithArguments(fullName);

    private static DiagnosticResult ExpectToday()
        => AnalyzerVerifier.Diagnostic(DiagnosticDescriptors.AvoidDateTimeToday);

    // =========================================================================
    // Should report PRAG0900 — DateTime/DateTimeOffset Now/UtcNow
    // =========================================================================

    [Fact]
    public async Task DateTimeNow_Direct_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var now = {|#0:DateTime.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectNow("DateTime.Now").WithLocation(0));
    }

    [Fact]
    public async Task DateTimeUtcNow_Direct_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var now = {|#0:DateTime.UtcNow|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectNow("DateTime.UtcNow").WithLocation(0));
    }

    [Fact]
    public async Task DateTimeOffsetNow_Direct_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var now = {|#0:DateTimeOffset.Now|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectNow("DateTimeOffset.Now").WithLocation(0));
    }

    [Fact]
    public async Task DateTimeOffsetUtcNow_Direct_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var now = {|#0:DateTimeOffset.UtcNow|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectNow("DateTimeOffset.UtcNow").WithLocation(0));
    }

    // =========================================================================
    // Should report PRAG0901 — DateTime.Today
    // =========================================================================

    [Fact]
    public async Task DateTimeToday_Direct_ReportsWarning()
    {
        var test = @"
using System;

class TestClass
{
    void Method()
    {
        var today = {|#0:DateTime.Today|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, ExpectToday().WithLocation(0));
    }

    // =========================================================================
    // Should NOT report — clean patterns
    // =========================================================================

    [Fact]
    public async Task InjectedClockUsage_NoDiagnostic()
    {
        var test = @"
using System;

interface IClock
{
    DateTimeOffset UtcNow { get; }
}

class TestClass
{
    private readonly IClock _clock;

    TestClass(IClock clock) => _clock = clock;

    void Method()
    {
        var now = _clock.UtcNow;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NowPropertyOnOtherType_NoDiagnostic()
    {
        var test = @"
class Scheduler
{
    public static string Now => ""custom"";
    public static string UtcNow => ""custom"";
    public static string Today => ""custom"";
}

class TestClass
{
    void Method()
    {
        var a = Scheduler.Now;
        var b = Scheduler.UtcNow;
        var c = Scheduler.Today;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InstanceDateProperty_NoDiagnostic()
    {
        var test = @"
using System;

class TestClass
{
    void Method(DateTime value)
    {
        var date = value.Date;
        var ticks = value.Ticks;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
