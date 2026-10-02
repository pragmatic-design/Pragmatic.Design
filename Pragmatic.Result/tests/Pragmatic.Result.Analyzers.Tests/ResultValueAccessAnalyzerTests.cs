using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Result.Analyzers.ResultValueAccessAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Result.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="ResultValueAccessAnalyzer"/> (PRAG0001).
///     Uses inline Result stubs so the analyzer resolves Pragmatic.Result types
///     without requiring a full project reference in the test compilation.
/// </summary>
public class ResultValueAccessAnalyzerTests
{
    /// <summary>
    ///     Minimal Result stubs that match the real Pragmatic.Result type signatures
    ///     the analyzer checks: namespace, type name, IsSuccess, IsFailure, Value.
    /// </summary>
    private const string ResultStub = @"
namespace Pragmatic.Result
{
    public interface IError
    {
        string Code { get; }
        int StatusCode { get; }
    }

    public record Error : IError
    {
        public virtual string Code => ""ERROR"";
        public virtual int StatusCode => 500;
    }

    public readonly struct Result<TValue, TError> where TError : IError
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public TValue Value { get; }
        public TError Error { get; }

        public static Result<TValue, TError> Success(TValue value) => default;
        public static Result<TValue, TError> Failure(TError error) => default;
    }

    public readonly struct Result<TValue>
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public TValue Value { get; }
        public Error Error { get; }
    }
}
";

    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic("PRAG0001");

    // =========================================================================
    // PRAG0001 — should be reported
    // =========================================================================

    [Fact]
    public async Task DirectAccess_NoPriorCheck_ReportsWarning()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        var value = result.{|#0:Value|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("result"));
    }

    [Fact]
    public async Task AliasedVariable_Unguarded_ReportsWarning()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        var r = result;
        var value = r.{|#0:Value|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("r"));
    }

    // =========================================================================
    // Safe patterns — NO diagnostic expected
    // =========================================================================

    [Fact]
    public async Task InsideIsSuccessIf_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (result.IsSuccess)
        {
            var value = result.Value;
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InsideIsFailureElse_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (result.IsFailure)
        {
            // handle error
        }
        else
        {
            var value = result.Value;
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InsideTernary_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    int Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        return result.IsSuccess ? result.Value : -1;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EarlyReturnOnIsFailure_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    Pragmatic.Result.Result<int, Pragmatic.Result.Error> Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (result.IsFailure) return result;
        var value = result.Value;
        return result;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EarlyThrowOnIsFailure_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (result.IsFailure) throw new System.Exception();
        var value = result.Value;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task EarlyReturnOnNegatedIsSuccess_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    Pragmatic.Result.Result<int, Pragmatic.Result.Error> Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (!result.IsSuccess) { return result; }
        var value = result.Value;
        return result;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task VariableAlias_GuardedByIsSuccess_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        var r = result;
        if (r.IsSuccess)
        {
            var value = r.Value;
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PatternMatching_IsSuccessTrue_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (result is { IsSuccess: true })
        {
            var value = result.Value;
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NegatedIsFailure_NoDiagnostic()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var result = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        if (!result.IsFailure)
        {
            var value = result.Value;
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    // =========================================================================
    // Mixed — only unguarded accesses reported
    // =========================================================================

    [Fact]
    public async Task MultipleResults_OnlyUnguardedReported()
    {
        var test = ResultStub + @"
class TestClass
{
    void Method()
    {
        var guarded = new Pragmatic.Result.Result<int, Pragmatic.Result.Error>();
        var unguarded = new Pragmatic.Result.Result<string, Pragmatic.Result.Error>();

        if (guarded.IsSuccess)
        {
            var v1 = guarded.Value;   // safe — guarded
        }

        var v2 = unguarded.{|#0:Value|};  // unsafe — not guarded
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("unguarded"));
    }
}
