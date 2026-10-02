using Microsoft.CodeAnalysis.Testing;
using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeWithoutKindAnalyzer,
    Pragmatic.Temporal.CodeFixers.DateTimeWithoutKindCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
    Pragmatic.Temporal.Analyzers.DateTimeWithoutKindAnalyzer,
    Pragmatic.Temporal.CodeFixers.DateTimeWithoutKindCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.CodeFixers.Tests;

public class DateTimeWithoutKindCodeFixTests
{
    [Fact]
    public async Task SixArgumentCtor_FirstAction_AppendsUtcKind()
    {
        var testCode = @"
using System;
class C
{
    void M()
    {
        var dt = {|PRAG0902:new DateTime(2024, 1, 15, 10, 30, 0)|};
    }
}";
        var fixedCode = @"
using System;
class C
{
    void M()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task SixArgumentCtor_SecondAction_AppendsLocalKind()
    {
        var test = new CodeFixTest
        {
            TestCode = @"
using System;
class C
{
    void M()
    {
        var dt = {|PRAG0902:new DateTime(2024, 1, 15, 10, 30, 0)|};
    }
}",
            FixedCode = @"
using System;
class C
{
    void M()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Local);
    }
}",
            CodeActionIndex = 1
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task SixArgumentCtor_ThirdAction_AppendsUnspecifiedKind()
    {
        var test = new CodeFixTest
        {
            TestCode = @"
using System;
class C
{
    void M()
    {
        var dt = {|PRAG0902:new DateTime(2024, 1, 15, 10, 30, 0)|};
    }
}",
            FixedCode = @"
using System;
class C
{
    void M()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Unspecified);
    }
}",
            CodeActionIndex = 2
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task ThreeArgumentCtor_ExpandsToSevenArgumentOverloadWithKind()
    {
        // There is no (y, m, d, kind) overload — the fix must expand to midnight + kind.
        var testCode = @"
using System;
class C
{
    void M()
    {
        var dt = {|PRAG0902:new DateTime(2024, 1, 15)|};
    }
}";
        var fixedCode = @"
using System;
class C
{
    void M()
    {
        var dt = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task TicksCtor_AppendsKind()
    {
        var testCode = @"
using System;
class C
{
    void M()
    {
        var dt = {|PRAG0902:new DateTime(638400000000000000L)|};
    }
}";
        var fixedCode = @"
using System;
class C
{
    void M()
    {
        var dt = new DateTime(638400000000000000L, DateTimeKind.Utc);
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task QualifiedTypeName_EmitsQualifiedDateTimeKind()
    {
        // No using System — the emitted DateTimeKind must be qualified to compile.
        var testCode = @"
class C
{
    void M()
    {
        var dt = {|PRAG0902:new System.DateTime(2024, 1, 15)|};
    }
}";
        var fixedCode = @"
class C
{
    void M()
    {
        var dt = new System.DateTime(2024, 1, 15, 0, 0, 0, System.DateTimeKind.Utc);
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task CalendarCtor_NoFixOffered()
    {
        // Inserting a kind into a Calendar-based ctor would require reordering — no fix.
        var testCode = @"
using System;
using System.Globalization;
class C
{
    void M()
    {
        var dt = {|PRAG0902:new DateTime(2024, 1, 15, new GregorianCalendar())|};
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, testCode);
    }
}
