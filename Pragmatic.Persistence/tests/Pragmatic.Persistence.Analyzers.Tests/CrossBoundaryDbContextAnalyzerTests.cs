using Microsoft.CodeAnalysis.Testing;
using Xunit;
using AnalyzerVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Pragmatic.Persistence.Analyzers.CrossBoundaryDbContextAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Pragmatic.Persistence.Analyzers.Tests;

/// <summary>
///     Tests for <see cref="CrossBoundaryDbContextAnalyzer"/> (PRAG0686). Stubs the generated
///     [PragmaticDbContext] attribute and a Catalog-owned DbContext; verifies reach-in from another
///     boundary is flagged while same-boundary use is clean.
/// </summary>
public class CrossBoundaryDbContextAnalyzerTests
{
    private const string Stub = @"
namespace Pragmatic.Persistence.EFCore
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class PragmaticDbContextAttribute : System.Attribute
    {
        public PragmaticDbContextAttribute(string boundary) { }
    }
}
namespace App.Catalog
{
    [Pragmatic.Persistence.EFCore.PragmaticDbContext(""Catalog"")]
    public class CatalogDbContext { }
}
";

    [Fact]
    public async Task ServiceInOtherBoundary_InjectsDbContext_ReportsDiagnostic()
    {
        var test = Stub + @"
namespace App.Booking
{
    public class ReservationService
    {
        public ReservationService(App.Catalog.CatalogDbContext {|#0:db|}) { }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            AnalyzerVerifier.Diagnostic("PRAG0686").WithLocation(0)
                .WithArguments("ReservationService", "CatalogDbContext", "Catalog"));
    }

    [Fact]
    public async Task ServiceInSameBoundary_InjectsDbContext_NoDiagnostic()
    {
        var test = Stub + @"
namespace App.Catalog
{
    public class AmenityService
    {
        public AmenityService(CatalogDbContext db) { }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ServiceInOtherBoundary_DbContextField_ReportsDiagnostic()
    {
        var test = Stub + @"
namespace App.Billing
{
    public class InvoiceService
    {
        private App.Catalog.CatalogDbContext {|#0:_db|};
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(
            test,
            AnalyzerVerifier.Diagnostic("PRAG0686").WithLocation(0)
                .WithArguments("InvoiceService", "CatalogDbContext", "Catalog"));
    }

    [Fact]
    public async Task NonDbContextInjection_NoDiagnostic()
    {
        var test = Stub + @"
namespace App.Booking
{
    public class ReservationService
    {
        public ReservationService(string connectionName) { }
    }
}";
        await AnalyzerVerifier.VerifyAnalyzerAsync(test);
    }
}
