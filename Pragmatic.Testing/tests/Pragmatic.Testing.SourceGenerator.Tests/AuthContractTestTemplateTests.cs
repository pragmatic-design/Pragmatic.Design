using System.Collections.Generic;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Templates;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies #7 phase-1 codegen: a boundary's endpoints produce authorization contract tests asserting a
///     caller without the permission is rejected and one with it reaches the endpoint, against the
///     Pragmatic.Testing harness.
/// </summary>
public class AuthContractTestTemplateTests
{
    private static EndpointContractModel Endpoint(string action, string verb, string route, string? permission) =>
        new() { Boundary = "Billing", ActionName = action, HttpMethod = verb, Route = route, Permission = permission };

    private static string Render(params EndpointContractModel[] endpoints) =>
        new AuthContractTestTemplate("Billing", endpoints).RenderOutput().Text;

    [Fact]
    public void SecuredEndpoint_GeneratesRejectAndReachTests()
    {
        var source = Render(Endpoint("GetInvoice", "Get", "/api/invoices/{id}", "billing.invoice.read"));

        source.Should().Contain("public sealed partial class BillingAuthContractTests : global::Pragmatic.Testing.PragmaticContractTestBase");
        source.Should().Contain("GetInvoice_WithoutRequiredPermission_IsRejected");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeRejected(response);");
        source.Should().Contain("GetInvoice_WithRequiredPermission_IsReachable");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticTestIdentity.AsUser(request, \"contract-billing.invoice.read\", permissions: [\"billing.invoice.read\"]);");
        source.Should().Contain(
            "global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeAuthorizedUnlike(response, deniedResponse);");
        source.Should().Contain("new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Get, \"/api/invoices/\" + global::System.Guid.NewGuid() + \"\");");
    }

    /// <summary>
    ///     The reachable test sends the unprivileged request too: the pair is the measurement.
    /// </summary>
    /// <remarks>
    ///     ⚠️ "Not forbidden" alone is satisfied by "never got there". When a middleware refuses every
    ///     request with 400, the unprivileged call counts as rejected and the privileged one counts as
    ///     not forbidden — fifty-four generated tests reported success on exactly that. Two identical
    ///     statuses now fail, and the only way to compare is to send both.
    /// </remarks>
    [Fact]
    public void TheReachableTest_SendsBothCallers()
    {
        var source = Render(Endpoint("GetInvoice", "Get", "/api/invoices/{id}", "billing.invoice.read"));

        source.Should().Contain("global::Pragmatic.Testing.PragmaticTestIdentity.AsUser(denied, \"contract-noperm\");")
            .And.Contain("var deniedResponse = await Client.SendAsync(denied);");
    }

    /// <summary>
    ///     Every contract request passes through the application before it is sent.
    /// </summary>
    /// <remarks>
    ///     A permission is not always the whole authority: a tenancy operation needs a tenant, and a host
    ///     that derives permissions from roles stops honouring raw permission claims. Neither is module
    ///     metadata, so the generator cannot know it and hands the request over instead.
    ///     <para>
    ///         ⚠️ With the <b>boundary</b> as well as the operation. An application of several
    ///         services signs with a different key per service and maps a different role table, and the
    ///         request itself does not say whose it is — deciding it by matching a route prefix sends a
    ///         route the table does not name to the wrong host.
    ///     </para>
    /// </remarks>
    [Fact]
    public void EveryRequest_IsHandedToTheApplicationFirst_WithItsBoundary()
    {
        var source = Render(Endpoint("GetInvoice", "Get", "/api/invoices/{id}", "billing.invoice.read"));

        source.Should().Contain(
            "global::Pragmatic.Testing.PragmaticContractHost.Prepare(request, \"GetInvoice\", Boundary);");
    }

    /// <summary>
    ///     The class says which boundary it is, because the generator knows and nothing downstream does.
    /// </summary>
    /// <remarks>
    ///     It is what <c>PragmaticContractTestBase.Client</c> resolves the per-service client
    ///     with, and what reaches the application's <c>PrepareRequest</c>. The boundary was already in
    ///     the class's own name (<c>BillingAuthContractTests</c>) and readable by nobody: a name is not a
    ///     value.
    /// </remarks>
    [Fact]
    public void TheGeneratedClass_DeclaresTheBoundaryItBelongsTo()
    {
        var source = Render(Endpoint("GetInvoice", "Get", "/api/invoices/{id}", "billing.invoice.read"));

        source.Should().Contain("protected override string Boundary => \"Billing\";");
    }

    [Fact]
    public void GetByIdEndpoint_AlsoGeneratesNotFoundTest_WithRandomId()
    {
        var source = Render(Endpoint("GetInvoice", "Get", "/api/invoices/{id}", "billing.invoice.read"));

        source.Should().Contain("GetInvoice_WithUnknownId_IsNotFound");
        source.Should().Contain("new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Get, \"/api/invoices/\" + global::System.Guid.NewGuid() + \"\");");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeNotFound(response);");
    }

    /// <summary>
    ///     A sub-collection gets no not-found contract: a route parameter does not make a get-by-id.
    /// </summary>
    /// <remarks>
    ///     <c>GET /api/invoices/{id}/payments</c> of an invoice that matches nothing answers <b>200 with an
    ///     empty list</b>, which is the right answer — the filter matched no rows. Asserting 404 there asks
    ///     the application to be wrong. Measured: it was the one red in a suite of 136
    ///     generated and hand-written tests, and the application was right.
    /// </remarks>
    [Fact]
    public void ACollectionRead_GeneratesNoNotFoundTest_EvenWithARouteParameter()
    {
        var source = Render(new EndpointContractModel
        {
            Boundary = "Billing",
            ActionName = "ListInvoicePayments",
            HttpMethod = "Get",
            Route = "/api/invoices/{id}/payments",
            Permission = "billing.invoice.read",
            AnswersWithACollection = true
        });

        source.Should().NotContain("ListInvoicePayments_WithUnknownId_IsNotFound");
        source.Should().Contain("ListInvoicePayments_WithRequiredPermission_IsReachable",
            "the control: the authorization pair is still generated for it");
    }

    [Fact]
    public void AnonymousEndpoint_GeneratesNoAuthTests()
    {
        var source = Render(Endpoint("HealthCheck", "Get", "/health", permission: null));

        source.Should().NotContain("HealthCheck_WithoutRequiredPermission_IsRejected");
        source.Should().NotContain("HealthCheck_WithRequiredPermission_IsReachable");
    }

    [Fact]
    public void MultipleEndpoints_EachGetTheirTests()
    {
        var source = Render(
            Endpoint("GetInvoice", "Get", "/api/invoices/{id}", "billing.invoice.read"),
            Endpoint("CreateInvoice", "Post", "/api/invoices", "billing.invoice.create"));

        source.Should().Contain("GetInvoice_WithoutRequiredPermission_IsRejected");
        source.Should().Contain("CreateInvoice_WithoutRequiredPermission_IsRejected");
        source.Should().Contain("global::System.Net.Http.HttpMethod.Post, \"/api/invoices\"");
    }
}
