using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Templates;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies #7 phase-2 CRUD codegen: a create endpoint produces a round-trip test posting a synthesized
///     body and asserting success, against the Pragmatic.Testing harness.
/// </summary>
public class CrudContractTestTemplateTests
{
    private static string Render(CrudCreateModel create) =>
        new CrudContractTestTemplate("Billing", [create]).RenderOutput().Text;

    private static readonly CrudCreateModel CreateInvoice = new()
    {
        Boundary = "Billing",
        OperationName = "RaiseInvoice",
        Route = "/api/invoices",
        Permission = "billing.invoice.create",
        CanSynthesizeBody = true,
        Fields = new EquatableArray<CrudFieldModel>(
        [
            new CrudFieldModel { Name = "Number", ValueExpression = "\"test-\" + global::System.Guid.NewGuid()" },
            new CrudFieldModel { Name = "Amount", ValueExpression = "1.0m" }
        ])
    };

    [Fact]
    public void GeneratesCreateTest_WithSynthesizedBody_AndSuccessAssertion()
    {
        var source = Render(CreateInvoice);

        source.Should().Contain("public sealed partial class BillingCrudContractTests : global::Pragmatic.Testing.PragmaticContractTestBase");
        source.Should().Contain("public async global::System.Threading.Tasks.Task CreateRaiseInvoice_WithValidBody_IsCreated()");
        source.Should().Contain("var body = new");
        source.Should().Contain("Number = \"test-\" + global::System.Guid.NewGuid(),");
        source.Should().Contain("Amount = 1.0m,");
        source.Should().Contain("new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Post, \"/api/invoices\");");
        source.Should().Contain("\"billing.invoice.create\"");
        source.Should().Contain(
            "request.Content = global::System.Net.Http.Json.JsonContent.Create("
            + "global::Pragmatic.Testing.PragmaticContractHost.Body(\"RaiseInvoice\", body));");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeSuccess(response);");
    }

    /// <summary>
    ///     An empty body omits the required fields, and the response is 422.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A validator refusing a request it understood answers 422; 400 is for a request that could
    ///     not be read at all. The generated test asserts what the API really does: if the two diverged,
    ///     every generated contract test would pass against a contract that does not exist.
    /// </remarks>
    [Fact]
    public void GeneratesValidationTest_PostingEmptyBody_ExpectsRejection()
    {
        var source = Render(CreateInvoice);

        source.Should().Contain("CreateRaiseInvoice_WithMissingRequiredFields_IsRejected");
        source.Should().Contain("global::System.Net.Http.Json.JsonContent.Create(new { });");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeRejected(response);");
    }

    /// <summary>
    ///     A body the generator cannot fill is emitted all the same, asking the application for
    ///     one. Dropping the tests instead would drop with them the tenant-isolation proof of every
    ///     aggregate whose create carries its children.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>PragmaticContractHost.BodyFor</c> exists for precisely this — "a create whose validity
    ///         depends on more than the shape" — and it is read <b>inside</b> the create and isolation
    ///         tests. If those were not emitted, the hook could not rescue a test that does not exist,
    ///         and nothing would tell the consumer anything was missing.
    ///     </para>
    ///     <para>
    ///         So the synthesised body is <c>null!</c> and <c>Body</c> throws a message naming the create:
    ///         a test that fails asking for a body is strictly better than a test nobody knows about.
    ///     </para>
    /// </remarks>
    [Fact]
    public void UnsynthesizableBody_StillEmitsTheCreate_AskingTheApplicationForABody()
    {
        var source = Render(CreateInvoice with { CanSynthesizeBody = false });

        source.Should().Contain("CreateRaiseInvoice_WithValidBody_IsCreated");
        source.Should().Contain("object? body = null;",
            "there is no shape to fill it from, so the application is the only one who can");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticContractHost.Body(\"RaiseInvoice\", body)");
        source.Should().NotContain("var body = new",
            "half a body would be refused by the application for the wrong reason");
        source.Should().Contain("CreateRaiseInvoice_WithMissingRequiredFields_IsRejected");
    }

    /// <summary>
    ///     And the isolation test, which is the one the multi-tenancy skill calls the proof that isolation
    ///     works. An aggregate whose create carries its children is the shape the framework teaches, so
    ///     this was missing for exactly the entities most likely to carry a tenant's money.
    /// </summary>
    [Fact]
    public void UnsynthesizableBody_OnATenantEntity_StillEmitsTheIsolationTest()
        => Render(CreateInvoice with { CanSynthesizeBody = false, IsTenantScoped = true })
            .Should().Contain("CreateRaiseInvoice_IsNotVisibleToAnotherTenant");

    [Fact]
    public void TenantScopedEntity_GeneratesCrossTenantIsolationTest()
    {
        var source = Render(CreateInvoice with { IsTenantScoped = true });

        source.Should().Contain("CreateRaiseInvoice_IsNotVisibleToAnotherTenant");
        source.Should().Contain("AsUser(createRequest, \"contract-billing.invoice.create\", \"tenant-a\"");
        source.Should().Contain("var location = createResponse.Headers.Location?.ToString();");
        source.Should().Contain(
            "AsUser(readRequest, \"contract-billing.invoice.create\", \"tenant-b\", null, "
            + "permissions: [\"billing.invoice.create\"]);");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeNotFound(readResponse);");
    }

    /// <summary>
    ///     The tenant-B reader holds the permission of the read that answers the Location, not only the
    ///     create's.
    /// </summary>
    /// <remarks>
    ///     With the create's permission alone the read is refused 403 before the lookup runs, and 404 — the
    ///     isolation this test exists to prove — is never reached. It stayed hidden because a create
    ///     answered without a Location whenever no single read of the entity was declared, and the test
    ///     returned before its read.
    /// </remarks>
    [Fact]
    public void TenantIsolationRead_HoldsTheReadPermission()
    {
        var source = Render(CreateInvoice with { IsTenantScoped = true, ReadPermission = "billing.invoice.read" });

        source.Should().Contain(
            "AsUser(readRequest, \"contract-billing.invoice.create\", \"tenant-b\", null, "
            + "permissions: [\"billing.invoice.create\", \"billing.invoice.read\"]);");
    }

    [Fact]
    public void NonTenantEntity_GeneratesNoIsolationTest()
    {
        Render(CreateInvoice).Should().NotContain("IsNotVisibleToAnotherTenant");
    }
}
