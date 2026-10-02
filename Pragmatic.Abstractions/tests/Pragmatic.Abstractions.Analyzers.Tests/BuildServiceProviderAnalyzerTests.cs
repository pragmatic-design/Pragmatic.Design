using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Abstractions.Analyzers.BuildServiceProviderAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Abstractions.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="BuildServiceProviderAnalyzer"/> (PRAG1451). Inline stub reproduces the
///     IServiceCollection.BuildServiceProvider() extension the analyzer keys off.
/// </summary>
public class BuildServiceProviderAnalyzerTests
{
    private const string Stub = @"using Microsoft.Extensions.DependencyInjection;
namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection { }
    public class ServiceProvider { }
    public static class ServiceCollectionContainerBuilderExtensions
    {
        public static ServiceProvider BuildServiceProvider(this IServiceCollection services) => null;
    }
}
";

    private static DiagnosticResult Expect() => AnalyzerVerifier.Diagnostic("PRAG1451");

    [Fact]
    public async Task BuildServiceProvider_OnServiceCollection_ReportsDiagnostic()
    {
        var test = Stub + @"
class Startup
{
    void Configure(IServiceCollection services)
    {
        var provider = {|#0:services.BuildServiceProvider()|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test, Expect().WithLocation(0));
    }

    [Fact]
    public async Task UnrelatedBuildServiceProvider_NoDiagnostic()
    {
        // Same method name, different (non-DI) containing type → not flagged.
        var test = @"
class Container
{
    public object BuildServiceProvider() => null;
    void M() { var x = BuildServiceProvider(); }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
