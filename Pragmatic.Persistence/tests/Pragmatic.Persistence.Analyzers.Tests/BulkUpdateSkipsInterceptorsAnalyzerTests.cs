using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.BulkUpdateSkipsInterceptorsAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

/// <summary>
///     PRAG0688 — the twin of PRAG0687, on the operation that touches the most rows.
/// </summary>
/// <remarks>
///     <c>ExecuteUpdate</c> is translated straight to SQL and never enters the change tracker, so
///     nothing that hangs off SaveChanges happens: the audit columns keep their old values and the
///     concurrency token does not move, which leaves a client holding a stale copy able to save over
///     the change believing it fresh. The value moves, the audit column does not, and nothing else
///     warns.
/// </remarks>
public class BulkUpdateSkipsInterceptorsAnalyzerTests
{
    private const string Stub = @"
using System;
using System.Linq;
namespace Pragmatic.Persistence.Entity
{
    [AttributeUsage(AttributeTargets.Class)]
    public class AuditableAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class ConcurrencyAwareAttribute : Attribute { }
}
namespace Microsoft.EntityFrameworkCore
{
    public static class RelationalQueryableExtensions
    {
        public static int ExecuteUpdate<T>(this IQueryable<T> source, object setters) => 0;
        public static int ExecuteDelete<T>(this IQueryable<T> source) => 0;
    }
}
";

    [Fact]
    public async Task ExecuteUpdate_OnAnAuditableEntity_IsReported()
    {
        var test = Stub + @"
[Pragmatic.Persistence.Entity.Auditable]
public class Term { public int Occurrences { get; set; } }

class Recalibrate
{
    void Run(IQueryable<Term> terms)
    {
        {|#0:Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteUpdate(terms, null)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            AnalyzerVerifier.Diagnostic("PRAG0688").WithLocation(0).WithArguments("Term", "[Auditable]"));
    }

    /// <summary>
    ///     The control: an entity that declares neither is not reported.
    /// </summary>
    /// <remarks>
    ///     Without it, an analyzer that fired on every <c>ExecuteUpdate</c> would pass the test above
    ///     and turn a legitimate bulk write into noise everybody learns to ignore.
    /// </remarks>
    [Fact]
    public async Task ExecuteUpdate_OnAPlainEntity_IsNotReported()
    {
        var test = Stub + @"
public class Reading { public int Value { get; set; } }

class Recalibrate
{
    void Run(IQueryable<Reading> readings)
    {
        Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteUpdate(readings, null);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    /// <summary>And a delete is not this analyzer's business: PRAG0687 already covers it.</summary>
    [Fact]
    public async Task ExecuteDelete_IsLeftToItsOwnAnalyzer()
    {
        var test = Stub + @"
[Pragmatic.Persistence.Entity.Auditable]
public class Term { public int Occurrences { get; set; } }

class Purge
{
    void Run(IQueryable<Term> terms)
    {
        Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteDelete(terms);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
