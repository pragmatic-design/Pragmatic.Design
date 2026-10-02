using System.IO;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Testing.SourceGenerator;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies the #7 phase-1 generator pipeline end to end: from an app's <c>[Endpoint]</c>/<c>[EndpointGroup]</c>
///     to the generated authorization contract test class.
/// </summary>
public class ContractTestGeneratorTests
{
    private const string Source = """
        namespace Pragmatic.Endpoints.Attributes
        {
            public enum HttpVerb { Get, Post, Put, Patch, Delete }
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(HttpVerb method, string route) { }
            }
            public sealed class EndpointGroupAttribute : System.Attribute
            {
                public EndpointGroupAttribute(string routePrefix) { }
            }
            public sealed class EndpointGroupAttribute<TGroup> : System.Attribute where TGroup : class { }
        }
        namespace Pragmatic.Authorization
        {
            public sealed class RequirePermissionAttribute : System.Attribute
            {
                public RequirePermissionAttribute(string permission) { }
            }
        }
        namespace Pragmatic.Actions.Mutation
        {
            public abstract class Mutation<TEntity> { }
        }
        namespace App.Billing.Entities
        {
            public class Invoice { }
        }
        namespace App.Billing.Endpoints
        {
            [Pragmatic.Endpoints.Attributes.EndpointGroup("/api/invoices")]
            public sealed class InvoicesGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Get, "/{id}")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.read")]
            public class GetInvoiceEndpoint { }

            // A real create is a Mutation<TEntity>; the generator only emits the round-trip test for those.
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<InvoicesGroup>]
            [Pragmatic.Authorization.RequirePermission("billing.invoice.create")]
            public class CreateInvoiceEndpoint : Pragmatic.Actions.Mutation.Mutation<App.Billing.Entities.Invoice>
            {
                public string Number { get; set; }
                public decimal Amount { get; set; }
            }
        }
        """;

    private static string Generate(string fileNamePart, string source = Source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"))
        };
        var compilation = CSharpCompilation.Create("ContractGenTest", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ContractTestGenerator());
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(fileNamePart))?.GetText().ToString() ?? "";
    }

    [Fact]
    public void Generates_AuthContractTests_ForSecuredEndpoint()
    {
        var source = Generate("Billing.Auth");

        source.Should().Contain("class BillingAuthContractTests");
        source.Should().Contain("GetInvoice_WithoutRequiredPermission_IsRejected");
        source.Should().Contain("GetInvoice_WithRequiredPermission_IsReachable");
        source.Should().Contain("global::System.Net.Http.HttpMethod.Get, \"/api/invoices/\" + global::System.Guid.NewGuid() + \"\"");
        source.Should().Contain("\"billing.invoice.read\"");
    }

    [Fact]
    public void Generates_CrudRoundTripTest_ForPostEndpoint()
    {
        var source = Generate("Billing.Crud");

        source.Should().Contain("class BillingCrudContractTests");
        source.Should().Contain("CreateInvoice_WithValidBody_IsCreated");
        source.Should().Contain("Number = \"test-\" + global::System.Guid.NewGuid(),");
        source.Should().Contain("Amount = 1.0m,");
        source.Should().Contain("global::System.Net.Http.HttpMethod.Post, \"/api/invoices\"");
        source.Should().Contain("\"billing.invoice.create\"");
    }

    /// <summary>
    ///     The isolation read goes to the Location of the create — the entity's read by id — so its caller
    ///     holds that read's permission, found by correlating the GET on <c>{create route}/{param}</c>.
    /// </summary>
    /// <remarks>
    ///     With the create's permission alone a tenant-B read is 403 before any lookup, and the test cannot
    ///     observe the 404 it asserts.
    /// </remarks>
    [Fact]
    public void TenantIsolationRead_CarriesThePermissionOfTheEntitysReadById()
    {
        var tenantScoped = Source
            .Replace("public class Invoice { }", "public class Invoice : ITenantEntity { }")
            + """

            namespace App.Billing.Entities
            {
                public interface ITenantEntity { }
            }
            """;

        var source = Generate("Billing.Crud", tenantScoped);

        source.Should().Contain("CreateInvoice_IsNotVisibleToAnotherTenant");
        source.Should().Contain(
            "AsUser(readRequest, \"contract-billing.invoice.create\", \"tenant-b\", null, "
            + "permissions: [\"billing.invoice.create\", \"billing.invoice.read\"]);");
    }
}
