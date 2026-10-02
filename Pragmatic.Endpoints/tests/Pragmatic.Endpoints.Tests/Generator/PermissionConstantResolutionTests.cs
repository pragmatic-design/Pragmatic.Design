using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     PRAG0528: a permission constant that no producer in this compilation generates cannot be turned
///     into a permission value, so the endpoint would be mapped without it. That is fail-open, and the
///     build would otherwise stay silent about it.
/// </summary>
public class PermissionConstantResolutionTests : EndpointsGeneratorTestBase
{
    private const string UnknownConstantSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp;

        public record GhostResponse(string Data);

        [Endpoint(HttpVerb.Get, "/ghost")]
        [RequirePermission(GhostPermissions.Read)]
        public partial class GhostEndpoint : Endpoint<GhostResponse>
        {
            public override Task<Result<GhostResponse>> HandleAsync(CancellationToken ct = default)
            {
                return Task.FromResult<Result<GhostResponse>>(new GhostResponse("ok"));
            }
        }
        """;

    private const string DeclaredConstantSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        [assembly: Permission("billing.invoice.read", "Read invoices")]

        namespace TestApp;

        public record InvoiceResponse(string Data);

        [Endpoint(HttpVerb.Get, "/invoices")]
        [RequirePermission(BillingPermissions.Invoice.Read)]
        public partial class ListInvoicesEndpoint : Endpoint<InvoiceResponse>
        {
            public override Task<Result<InvoiceResponse>> HandleAsync(CancellationToken ct = default)
            {
                return Task.FromResult<Result<InvoiceResponse>>(new InvoiceResponse("ok"));
            }
        }
        """;

    [Fact]
    public void RequirePermission_WithUnknownConstant_ReportsPrag0528()
    {
        var result = RunGeneratorWithIdentity(UnknownConstantSource);

        HasDiagnostic(result, "PRAG0528").Should().BeTrue();
    }

    [Fact]
    public void RequirePermission_WithNestedDeclaredConstant_ResolvesAndReportsNothing()
    {
        var result = RunGeneratorWithIdentity(DeclaredConstantSource);

        HasDiagnostic(result, "PRAG0528").Should().BeFalse();

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("\"billing.invoice.read\"");
    }

    /// <summary>
    ///     A hand-written constant beside a generated one. The generated one cannot be bound in this run, Roslyn
    ///     drops every argument with it, and the hand-written constant was left as a path the catalogue does not
    ///     hold — PRAG0528, and a route mapped without that permission.
    /// </summary>
    [Fact]
    public void RequirePermission_WithAHandWrittenConstantBesideAGeneratedOne_KeepsBoth()
    {
        var result = RunGeneratorWithIdentity(DeclaredConstantSource
            .Replace("[RequirePermission(BillingPermissions.Invoice.Read)]",
                "[RequirePermission(LocalGrants.Export, BillingPermissions.Invoice.Read)]")
            + """

            public static class LocalGrants
            {
                public const string Export = "billing.invoice.export";
            }
            """);

        HasDiagnostic(result, "PRAG0528").Should().BeFalse();

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("\"billing.invoice.export\"");
        handler.Should().Contain("\"billing.invoice.read\"");
    }
}
