using Microsoft.CodeAnalysis.Testing;
using Xunit;
using NowCodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeNowAnalyzer,
    Pragmatic.Temporal.CodeFixers.DateTimeNowCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using TestsCodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Pragmatic.Temporal.Analyzers.DateTimeNowInTestsAnalyzer,
    Pragmatic.Temporal.CodeFixers.DateTimeNowCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Temporal.CodeFixers.Tests;

public class DateTimeNowCodeFixTests
{
    // Minimal IClock surface: the provider matches the interface by full name and the
    // emitted replacements only ever touch Now/UtcNow (DateTimeOffset members), so the
    // stub does not need DateOnly/TimeOnly/TimeProvider (absent from the default
    // reference assemblies of the test workspace).
    private const string ClockStub = @"
namespace Pragmatic.Temporal.Clock
{
    public interface IClock
    {
        System.DateTimeOffset UtcNow { get; }
        System.DateTimeOffset Now { get; }
    }
}
";

    [Fact]
    public async Task DateTimeNow_WithClockField_ReplacedWithNowLocalDateTime()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    DateTime M() => {|PRAG0900:DateTime.Now|};
}" + ClockStub;
        var fixedCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    DateTime M() => _clock.Now.LocalDateTime;
}" + ClockStub;
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task DateTimeUtcNow_WithClockField_ReplacedWithUtcNowUtcDateTime()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    DateTime M() => {|PRAG0900:DateTime.UtcNow|};
}" + ClockStub;
        var fixedCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    DateTime M() => _clock.UtcNow.UtcDateTime;
}" + ClockStub;
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task DateTimeToday_WithClockField_ReplacedWithLocalDate()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    DateTime M() => {|PRAG0901:DateTime.Today|};
}" + ClockStub;
        var fixedCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    DateTime M() => _clock.Now.LocalDateTime.Date;
}" + ClockStub;
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task DateTimeOffsetNow_WithClockProperty_ReplacedWithNow()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    public IClock Clock { get; set; }

    DateTimeOffset M() => {|PRAG0900:DateTimeOffset.Now|};
}" + ClockStub;
        var fixedCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    public IClock Clock { get; set; }

    DateTimeOffset M() => Clock.Now;
}" + ClockStub;
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task DateTimeOffsetUtcNow_WithPrimaryCtorParameter_ReplacedWithUtcNow()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C(IClock clock)
{
    DateTimeOffset M() => {|PRAG0900:DateTimeOffset.UtcNow|};
}" + ClockStub;
        var fixedCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C(IClock clock)
{
    DateTimeOffset M() => clock.UtcNow;
}" + ClockStub;
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task NoClockInScope_NoFixOffered()
    {
        var testCode = @"
using System;
class C
{
    DateTime M() => {|PRAG0900:DateTime.Now|};
}";
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, testCode);
    }

    [Fact]
    public async Task StaticMethod_InstanceClockField_NoFixOffered()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;
class C
{
    private readonly IClock _clock;
    public C(IClock clock) => _clock = clock;

    static DateTime M() => {|PRAG0900:DateTime.Now|};
}" + ClockStub;
        await NowCodeFixVerifier.VerifyCodeFixAsync(testCode, testCode);
    }

    [Fact]
    public async Task Prag0904_TestMethodWithClockField_SameFixApplies()
    {
        var testCode = @"
using System;
using Pragmatic.Temporal.Clock;

public class FactAttribute : Attribute { }

public class MyTests
{
    private readonly IClock _clock = null;

    [Fact]
    public void Test()
    {
        var now = {|PRAG0904:DateTimeOffset.UtcNow|};
    }
}" + ClockStub;
        var fixedCode = @"
using System;
using Pragmatic.Temporal.Clock;

public class FactAttribute : Attribute { }

public class MyTests
{
    private readonly IClock _clock = null;

    [Fact]
    public void Test()
    {
        var now = _clock.UtcNow;
    }
}" + ClockStub;
        await TestsCodeFixVerifier.VerifyCodeFixAsync(testCode, fixedCode);
    }

    [Fact]
    public async Task Prag0904_TestMethodWithoutClock_NoFixOffered()
    {
        var testCode = @"
using System;

public class FactAttribute : Attribute { }

public class MyTests
{
    [Fact]
    public void Test()
    {
        var now = {|PRAG0904:DateTime.UtcNow|};
    }
}";
        await TestsCodeFixVerifier.VerifyCodeFixAsync(testCode, testCode);
    }
}
