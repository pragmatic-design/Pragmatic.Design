using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Abstractions.Analyzers.CaptiveDependencyAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Abstractions.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="CaptiveDependencyAnalyzer"/> (PRAG1450). Inline stubs reproduce the
///     hosting/options/EF types the analyzer keys off (by metadata name), as empty types — the
///     analyzer matches type identity, not members.
/// </summary>
public class CaptiveDependencyAnalyzerTests
{
    private const string Stub = @"using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService { }
    public abstract class BackgroundService : IHostedService { }
}
namespace Microsoft.Extensions.Options
{
    public interface IOptionsSnapshot<T> { }
    public interface IOptionsMonitor<T> { }
}
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
}
public class Settings { }
";

    private static DiagnosticResult Expect() => AnalyzerVerifier.Diagnostic("PRAG1450");

    [Fact]
    public async Task BackgroundService_InjectsOptionsSnapshot_ReportsDiagnostic()
    {
        var test = Stub + @"
public class Worker : BackgroundService
{
    public Worker(IOptionsSnapshot<Settings> {|#0:options|}) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0)
            .WithArguments("Worker", "Microsoft.Extensions.Options.IOptionsSnapshot<Settings>", "IOptionsMonitor<T>"));
    }

    [Fact]
    public async Task BackgroundService_InjectsDbContext_ReportsDiagnostic()
    {
        var test = Stub + @"
public class AppDb : DbContext { }
public class Worker : BackgroundService
{
    public Worker(AppDb {|#0:db|}) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0)
            .WithArguments("Worker", "AppDb", "IServiceScopeFactory and create a scope per unit of work"));
    }

    [Fact]
    public async Task HostedService_InjectsOptionsSnapshot_ReportsDiagnostic()
    {
        var test = Stub + @"
public class Worker : IHostedService
{
    public Worker(IOptionsSnapshot<Settings> {|#0:options|}) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0)
            .WithArguments("Worker", "Microsoft.Extensions.Options.IOptionsSnapshot<Settings>", "IOptionsMonitor<T>"));
    }

    [Fact]
    public async Task BackgroundService_InjectsOptionsMonitor_NoDiagnostic()
    {
        var test = Stub + @"
public class Worker : BackgroundService
{
    public Worker(IOptionsMonitor<Settings> options) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RegularClass_InjectsOptionsSnapshot_NoDiagnostic()
    {
        var test = Stub + @"
public class NormalService
{
    public NormalService(IOptionsSnapshot<Settings> options) { }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
