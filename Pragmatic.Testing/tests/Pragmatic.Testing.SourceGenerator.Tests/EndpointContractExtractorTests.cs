using System.IO;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.SourceGenerator.Transforms;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies #7 endpoint extraction: verb, route (group prefix + own route) and required permission are read
///     from <c>[Endpoint]</c>/<c>[RequirePermission]</c> on the endpoint type.
/// </summary>
public class EndpointContractExtractorTests
{
    private const string Source = """
        namespace Pragmatic.Endpoints.Attributes
        {
            public enum HttpVerb { Get, Post, Put, Patch, Delete }
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(HttpVerb method, string route) { }
            }
        }
        namespace Pragmatic.Authorization
        {
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(string permission) { }
            }
            public sealed class RequireAnyPermissionAttribute : System.Attribute
            {
                public RequireAnyPermissionAttribute(params string[] permissions) { }
            }
        }
        namespace App.Billing.Endpoints
        {
            public class InvoicesGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Get, "/{id}")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.read")]
            public class GetInvoiceEndpoint { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Get, "/{id}/summary")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequireAnyPermission("billing.invoice.read", "booking.reservation.read")]
            public class SummariseInvoiceEndpoint { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "")]
            public class CreateInvoiceEndpoint { }
        }
        """;

    private static INamedTypeSymbol Type(string metadataName)
    {
        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };
        var compilation = CSharpCompilation.Create("ExtractorTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetTypeByMetadataName(metadataName)!;
    }

    [Fact]
    public void SecuredEndpoint_ExtractsVerbRouteAndPermission()
    {
        var model = EndpointContractExtractor.Extract(
            Type("App.Billing.Endpoints.GetInvoiceEndpoint"), groupRoutePrefix: "/api/invoices");

        model.Should().NotBeNull();
        model!.Boundary.Should().Be("Billing");
        model.ActionName.Should().Be("GetInvoice");
        model.HttpMethod.Should().Be("Get");
        model.Route.Should().Be("/api/invoices/{id}");
        model.Permission.Should().Be("billing.invoice.read");
        model.RequiresPermission.Should().BeTrue();
    }

    /// <summary>
    ///     An endpoint two audiences reach still has a contract.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Only <c>[RequirePermission]</c> was read, so declaring the OR form made the endpoint
    ///     anonymous as far as this generator was concerned and its two authorization cases stopped
    ///     being emitted — the Showcase lost two tests the day one endpoint moved to it, and the only
    ///     trace was the gate's count. One member of the set is enough for both claims the
    ///     generated pair makes.
    /// </remarks>
    [Fact]
    public void AnEndpointTwoPermissionsReach_StillDeclaresOne()
    {
        var model = EndpointContractExtractor.Extract(
            Type("App.Billing.Endpoints.SummariseInvoiceEndpoint"), groupRoutePrefix: "/api/invoices");

        model.Should().NotBeNull();
        model!.RequiresPermission.Should().BeTrue(
            "a route with an OR of two permissions is not a route anybody may call");
        model.Permission.Should().Be("billing.invoice.read");
    }

    [Fact]
    public void AnonymousEndpoint_HasNoPermission_AndCombinesEmptyRoute()
    {
        var model = EndpointContractExtractor.Extract(
            Type("App.Billing.Endpoints.CreateInvoiceEndpoint"), groupRoutePrefix: "/api/invoices");

        model.Should().NotBeNull();
        model!.HttpMethod.Should().Be("Post");
        model.Route.Should().Be("/api/invoices");
        model.Permission.Should().BeNull();
        model.RequiresPermission.Should().BeFalse();
    }
}
