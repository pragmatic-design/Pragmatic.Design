using Microsoft.CodeAnalysis.Testing;
using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeOffsetComparisonAnalyzer,
    Pragmatic.Temporal.CodeFixers.DateTimeOffsetComparisonCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.CodeFixers.Tests;

public class DateTimeOffsetComparisonCodeFixTests
{
    [Fact]
    public async Task LessThan_WrapsBothOperandsWithUtcDateTime()
    {
        var testCode = @"
using System;
class C
{
    bool M(DateTimeOffset a, DateTimeOffset b)
    {
        return a {|PRAG0903:<|} b;
    }
}";
        var fixedCode = @"
using System;
class C
{
    bool M(DateTimeOffset a, DateTimeOffset b)
    {
        return a.UtcDateTime < b.UtcDateTime;
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task GreaterThanOrEqual_MemberAccessOperands_Wrapped()
    {
        var testCode = @"
using System;
class Order { public DateTimeOffset CreatedAt { get; set; } }
class C
{
    bool M(Order x, Order y)
    {
        return x.CreatedAt {|PRAG0903:>=|} y.CreatedAt;
    }
}";
        var fixedCode = @"
using System;
class Order { public DateTimeOffset CreatedAt { get; set; } }
class C
{
    bool M(Order x, Order y)
    {
        return x.CreatedAt.UtcDateTime >= y.CreatedAt.UtcDateTime;
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task ConditionalOperand_ParenthesizedBeforeWrapping()
    {
        var testCode = @"
using System;
class C
{
    bool M(bool flag, DateTimeOffset a, DateTimeOffset b, DateTimeOffset c)
    {
        return (flag ? a : b) {|PRAG0903:<|} c;
    }
}";
        var fixedCode = @"
using System;
class C
{
    bool M(bool flag, DateTimeOffset a, DateTimeOffset b, DateTimeOffset c)
    {
        return (flag ? a : b).UtcDateTime < c.UtcDateTime;
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task MixedDateTimeOperand_NoFixOffered()
    {
        // The analyzer fires when either side is DateTimeOffset, but appending
        // .UtcDateTime to a plain DateTime would not compile — no fix.
        var testCode = @"
using System;
class C
{
    bool M(DateTimeOffset a, DateTime b)
    {
        return a {|PRAG0903:<|} b;
    }
}";
        await CodeFixVerifier.VerifyCodeFixAsync(testCode, testCode);
    }
}
