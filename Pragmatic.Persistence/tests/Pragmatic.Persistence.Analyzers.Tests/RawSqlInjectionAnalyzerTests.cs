using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.RawSqlInjectionAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="RawSqlInjectionAnalyzer"/> (PRAG0684).
///     Inline EF Core stubs reproduce the *Raw extension methods so the analyzer resolves them
///     without a real Microsoft.EntityFrameworkCore reference.
/// </summary>
public class RawSqlInjectionAnalyzerTests
{
    // The file-level `using` must precede the namespace declaration, so it lives at the top of the preamble.
    private const string Preamble = @"using Microsoft.EntityFrameworkCore;
namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<T> { }
    public class DatabaseFacade { }
    public static class RelationalQueryableExtensions
    {
        public static System.Collections.Generic.IEnumerable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) => null;
        public static System.Collections.Generic.IEnumerable<T> SqlQueryRaw<T>(this DatabaseFacade db, string sql, params object[] parameters) => null;
    }
    public static class RelationalDatabaseFacadeExtensions
    {
        public static int ExecuteSqlRaw(this DatabaseFacade db, string sql, params object[] parameters) => 0;
    }
}
";

    private static DiagnosticResult Expect()
        => AnalyzerVerifier.Diagnostic("PRAG0684");

    // =========================================================================
    // PRAG0684 — reported (non-constant SQL)
    // =========================================================================

    [Fact]
    public async Task FromSqlRaw_InterpolatedWithRuntimeValue_ReportsDiagnostic()
    {
        var test = Preamble + @"
class Repo
{
    void M(DbSet<object> set, string name)
    {
        set.FromSqlRaw({|#0:$""SELECT * FROM Users WHERE Name = '{name}'""|});
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0).WithArguments("FromSqlRaw"));
    }

    [Fact]
    public async Task ExecuteSqlRaw_Concatenation_ReportsDiagnostic()
    {
        var test = Preamble + @"
class Repo
{
    void M(DatabaseFacade db, string id)
    {
        db.ExecuteSqlRaw({|#0:""DELETE FROM T WHERE Id = "" + id|});
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0).WithArguments("ExecuteSqlRaw"));
    }

    [Fact]
    public async Task SqlQueryRaw_LocalVariable_ReportsDiagnostic()
    {
        var test = Preamble + @"
class Repo
{
    void M(DatabaseFacade db, string sql)
    {
        var r = db.SqlQueryRaw<int>({|#0:sql|});
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0).WithArguments("SqlQueryRaw"));
    }

    // =========================================================================
    // Safe patterns — NO diagnostic
    // =========================================================================

    [Fact]
    public async Task FromSqlRaw_ConstantLiteral_NoDiagnostic()
    {
        var test = Preamble + @"
class Repo
{
    void M(DbSet<object> set)
    {
        set.FromSqlRaw(""SELECT * FROM Users"");
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_InterpolatedAllConstantHoles_NoDiagnostic()
    {
        var test = Preamble + @"
class Repo
{
    const string Table = ""Users"";
    void M(DbSet<object> set)
    {
        set.FromSqlRaw($""SELECT * FROM {Table}"");
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FromSqlRaw_ConstField_NoDiagnostic()
    {
        var test = Preamble + @"
class Repo
{
    const string Sql = ""SELECT 1"";
    void M(DbSet<object> set)
    {
        set.FromSqlRaw(Sql);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UnrelatedMethodNamedFromSqlRaw_NoDiagnostic()
    {
        // Same method name, different (non-EF) containing type → not flagged.
        var test = @"
static class Other
{
    public static void FromSqlRaw(this object o, string sql) { }
}
class Repo
{
    void M(object o, string injected)
    {
        o.FromSqlRaw(injected);
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
