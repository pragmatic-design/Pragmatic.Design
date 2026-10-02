using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Result.Analyzers.ErrorPartialAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Result.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="ErrorPartialAnalyzer"/> (PRAG0002).
///     Uses inline Error stubs so the analyzer resolves <c>Pragmatic.Result.Error</c>
///     without requiring a full project reference in the test compilation.
/// </summary>
public class ErrorPartialAnalyzerTests
{
    /// <summary>
    ///     Minimal Error base stub matching the real <c>Pragmatic.Result.Error</c> shape the analyzer
    ///     checks: namespace, base-set properties, and the virtual <c>WriteExtensions</c> seam.
    /// </summary>
    private const string ErrorStub = @"
namespace System.Runtime.CompilerServices
{
    // Polyfill: 'init' accessors and positional records need IsExternalInit, which the analyzer
    // test harness's default reference set does not provide.
    internal static class IsExternalInit { }
}

namespace Pragmatic.Result
{
    using System.Collections.Generic;

    public interface IError
    {
        string Code { get; }
        int StatusCode { get; }
    }

    public abstract record Error : IError
    {
        public virtual string Code => ""ERROR"";
        public virtual int StatusCode => 500;
        public virtual string Title => """";
        public virtual void WriteExtensions(IDictionary<string, object?> extensions) { }
    }
}
";

    private static DiagnosticResult ExpectDiagnostic()
        => AnalyzerVerifier.Diagnostic("PRAG0002");

    // =========================================================================
    // PRAG0002 — should be reported
    // =========================================================================

    [Fact]
    public async Task NonPartialErrorWithCustomProperty_ReportsWarning()
    {
        var test = ErrorStub + @"
namespace Sample
{
    public sealed record {|#0:RoomUnavailableError|} : Pragmatic.Result.Error
    {
        public System.Guid RoomId { get; init; }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("RoomUnavailableError"));
    }

    [Fact]
    public async Task NonPartialErrorWithPositionalCustomProperty_ReportsWarning()
    {
        var test = ErrorStub + @"
namespace Sample
{
    public sealed record {|#0:PositionalError|}(System.Guid RoomId) : Pragmatic.Result.Error;
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("PositionalError"));
    }

    [Fact]
    public async Task NonPartialNonSealedErrorWithCustomProperty_ReportsWarning()
    {
        // Error base is a record, so error types are records too (a class cannot inherit a record).
        var test = ErrorStub + @"
namespace Sample
{
    public record {|#0:LegacyError|} : Pragmatic.Result.Error
    {
        public string? Reason { get; init; }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            ExpectDiagnostic().WithLocation(0).WithArguments("LegacyError"));
    }

    // =========================================================================
    // Safe patterns — NO diagnostic expected
    // =========================================================================

    [Fact]
    public async Task PartialErrorWithCustomProperty_NoDiagnostic()
    {
        var test = ErrorStub + @"
namespace Sample
{
    public sealed partial record RoomUnavailableError : Pragmatic.Result.Error
    {
        public System.Guid RoomId { get; init; }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PartialSplitOneDeclarationPartial_NoDiagnostic()
    {
        // Analyzer must not fire when ANY declaration carries 'partial'.
        var test = ErrorStub + @"
namespace Sample
{
    public sealed partial record SplitError : Pragmatic.Result.Error
    {
        public System.Guid RoomId { get; init; }
    }

    public sealed partial record SplitError
    {
        public string? Note { get; init; }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonPartialErrorWithoutCustomProperty_NoDiagnostic()
    {
        var test = ErrorStub + @"
namespace Sample
{
    public sealed record PlainError : Pragmatic.Result.Error
    {
        public override string Code => ""PLAIN"";
        public override int StatusCode => 400;
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonPartialErrorWithManualWriteExtensions_NoDiagnostic()
    {
        // Mirrors the 8 built-in HTTP errors: sealed record, NOT partial, custom properties,
        // but a hand-written WriteExtensions override → properties still reach the wire.
        var test = ErrorStub + @"
namespace Sample
{
    public sealed record NotFoundError : Pragmatic.Result.Error
    {
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }

        public override void WriteExtensions(System.Collections.Generic.IDictionary<string, object?> extensions)
        {
            extensions[""entityType""] = EntityType;
            extensions[""entityId""] = EntityId;
        }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TypeNotExtendingError_NoDiagnostic()
    {
        var test = ErrorStub + @"
namespace Sample
{
    public sealed record NotAnError
    {
        public System.Guid Id { get; init; }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
