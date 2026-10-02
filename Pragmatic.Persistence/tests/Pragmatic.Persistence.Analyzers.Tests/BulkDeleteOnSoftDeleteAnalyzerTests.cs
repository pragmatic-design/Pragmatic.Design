using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.BulkDeleteOnSoftDeleteAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

/// <summary>
///     PRAG0687 — the twin of PRAG0688, on the operation whose result cannot be undone.
/// </summary>
/// <remarks>
///     <c>ExecuteDelete</c> is translated straight to SQL and never enters the change tracker, so the
///     one thing <c>[SoftDelete]</c> exists to guarantee — that a delete marks the row instead of
///     removing it — does not happen. The rows are gone, and the query filter that was hiding them
///     makes the difference invisible: a read after the delete looks the same either way.
///     ⚠️ A descriptor that is defined and emitted is not proven to fire; these cases are what assert it.
/// </remarks>
public class BulkDeleteOnSoftDeleteAnalyzerTests
{
    private const string Stub = @"
using System;
using System.Linq;
namespace Pragmatic.Persistence.Entity
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SoftDeleteAttribute : Attribute { }
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
    public async Task ExecuteDelete_OnASoftDeleteEntity_IsReported()
    {
        var test = Stub + @"
[Pragmatic.Persistence.Entity.SoftDelete]
public class Term { public bool IsDeleted { get; set; } }

class Purge
{
    void Run(IQueryable<Term> terms)
    {
        {|#0:Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteDelete(terms)|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            AnalyzerVerifier.Diagnostic("PRAG0687").WithLocation(0).WithArguments("Term"));
    }

    /// <summary>
    ///     The control: an entity that is not soft-deletable is not reported.
    /// </summary>
    /// <remarks>
    ///     Without it, an analyzer that fired on every <c>ExecuteDelete</c> would pass the case above
    ///     and turn a legitimate bulk delete into noise everybody learns to ignore.
    /// </remarks>
    [Fact]
    public async Task ExecuteDelete_OnAPlainEntity_IsNotReported()
    {
        var test = Stub + @"
public class Reading { public int Value { get; set; } }

class Purge
{
    void Run(IQueryable<Reading> readings)
    {
        Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteDelete(readings);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    /// <summary>And an update is not this analyzer's business: PRAG0688 already covers it.</summary>
    [Fact]
    public async Task ExecuteUpdate_IsLeftToItsOwnAnalyzer()
    {
        var test = Stub + @"
[Pragmatic.Persistence.Entity.SoftDelete]
public class Term { public bool IsDeleted { get; set; } }

class Recalibrate
{
    void Run(IQueryable<Term> terms)
    {
        Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteUpdate(terms, null);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
