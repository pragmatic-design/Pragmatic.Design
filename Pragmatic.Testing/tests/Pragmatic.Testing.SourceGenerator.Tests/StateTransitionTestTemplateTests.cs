using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Templates;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies #7 state-transition codegen: a transition endpoint produces a create-then-POST flow asserting
///     2xx when the initial → target move is legal, or 409 when it is illegal — the doc's transition contract.
///     The complementary case walks the entity's other declared transitions to a source state where the
///     opposite answer is due; with no such walk it is not emitted at all (PRAG2363), never as a skipped test.
/// </summary>
public class StateTransitionTestTemplateTests
{
    private static string Render(StateTransitionModel t) =>
        new StateTransitionTestTemplate("Billing", [t]).RenderOutput().Text;

    private static StateTransitionModel Transition(bool initialIsLegal) => new()
    {
        Boundary = "Billing",
        EntityName = "Invoice",
        TransitionRoute = "/api/invoices/{id}/pay",
        CreateRoute = "/api/invoices",
        Permission = "billing.invoice.update",
        TargetState = "Paid",
        InitialState = "Draft",
        // Deliberately not "CreateInvoice": an operation name that spells "Create" + the entity's makes
        // the key this flow asks under indistinguishable from "Create" + EntityName, the wrong one.
        CreateOperation = "RaiseInvoice",
        InitialToTargetIsLegal = initialIsLegal,
        CreateFields = new EquatableArray<CrudFieldModel>(
        [
            new CrudFieldModel { Name = "Amount", ValueExpression = "1.0m" }
        ])
    };

    /// <summary>
    ///     A create that does not say where it put the entity fails the contract.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The generated flow returned quietly when the <c>Location</c> header was absent, which is a
    ///     green test that never reached the transition it exists to exercise.
    /// </remarks>
    [Fact]
    public void TheTransitionContract_DoesNotPassWhenItCannotFindTheEntity()
    {
        var source = Render(Transition(true));

        source.Should().Contain("ShouldIdentifyTheCreatedAsync(createResponse)",
            "an entity it cannot address means the transition was never posted, and that is a failure");
        source.Should().NotContain("return;",
            "a contract that returns early asserts nothing and reports success");
    }

    /// <summary>
    ///     Each request carries the permission its own endpoint declares.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The create was sent with the <b>transition's</b> permission, because the model carried one
    ///     and the template used it twice. The ordinary shape is two different ones — <c>x.create</c>
    ///     and <c>x.update</c> — so the arrangement was refused and the contract failed on the create,
    ///     which says nothing about the transition it exists to measure.
    ///     <para>
    ///         Unreachable while every transition was a skipped placeholder, no
    ///         generated create request was ever sent.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheCreateRequest_CarriesTheCreatesOwnPermission()
    {
        var source = Render(Transition(true) with { CreatePermission = "billing.invoice.create" });

        source.Should().Contain(
            "AsUser(createRequest, \"contract-invoice-flow\", permissions: [\"billing.invoice.create\"])",
            "the arrangement is refused unless it is made by someone allowed to create");
        source.Should().Contain(
            "AsUser(transitionRequest, \"contract-invoice-flow\", permissions: [\"billing.invoice.update\"])",
            "and the transition is still the transition's — one person, two authorities");
    }

    /// <summary>
    ///     The control: a create that declares none is sent as the unprivileged caller, not as the
    ///     transition's.
    /// </summary>
    /// <remarks>
    ///     Without it, "the create carries its own" would be satisfied by falling back to the
    ///     transition's whenever the create declares nothing — which is the defect, kept alive for the
    ///     case that produced it: the Showcase's create is gated by a policy class and declares no
    ///     permission at all.
    /// </remarks>
    [Fact]
    public void ACreateThatDeclaresNoPermission_IsNotGivenTheTransitions()
    {
        var source = Render(Transition(true) with { CreatePermission = null });

        source.Should().Contain("AsUser(createRequest, \"contract-invoice-flow\");");
        source.Should().NotContain(
            "AsUser(createRequest, \"contract-invoice-flow\", permissions: [\"billing.invoice.update\"]",
            "a permission the create never declared is not the create's");
    }

    /// <summary>
    ///     The caller's permission is passed as the array the identity helper takes.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It was passed as a bare string in the fourth position, which no overload accepts: every
    ///     transition contract that stopped being a skipped placeholder failed to compile. Unreachable
    ///     while every transition in the repository was a placeholder — the generated call was never
    ///     handed to a compiler.
    /// </remarks>
    [Fact]
    public void TheTransitionContract_PassesThePermissionAsAnArray()
    {
        var source = Render(Transition(true));

        source.Should().Contain("permissions: [\"billing.invoice.update\"]",
            "AsUser takes string[]; a bare string does not bind to any overload");
        source.Should().NotContain("\"contract-invoice-flow\", null, null,",
            "and the positional nulls that carried it are gone with it");
    }

    /// <summary>
    ///     The transition contract asks the application for the body and the identity, like the others.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Arranging an entity in its initial state means creating it, and a create whose validity
    ///     needs more than its shape — a foreign key to a row that must exist — cannot be synthesised.
    ///     Without the seam the auth and CRUD contracts have, an application has no way to make its own
    ///     transitions contractable, and no transition contract runs.
    /// </remarks>
    [Fact]
    public void TheTransitionContract_AsksTheApplicationForTheBodyAndTheIdentity()
    {
        var source = Render(Transition(true));

        source.Should().Contain(
            "global::Pragmatic.Testing.PragmaticContractHost.Body(\"RaiseInvoice\", body)",
            "the create that arranges the initial state is asked for under the create operation's own "
            + "name — the key its CRUD contract uses — so the application answers for it once");
        source.Should().Contain(
            "global::Pragmatic.Testing.PragmaticContractHost.Prepare(createRequest, \"RaiseInvoice\", Boundary)");
        source.Should().NotContain("\"CreateInvoice\"",
            "\"Create\" + the entity's name was a second key for one endpoint");
        source.Should().Contain(
            "global::Pragmatic.Testing.PragmaticContractHost.Prepare(transitionRequest, \"Invoice_TransitionToPaid\", Boundary)",
            "and the transition itself may need an authority the generator cannot name");
    }

    /// <summary>
    ///     A transition endpoint that takes a body gets one posted.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A request sent with its route and nothing else makes an endpoint with a required member
    ///     answer <b>415 Unsupported Media Type</b> before it can answer the 409 or the 2xx the contract
    ///     exists to measure — and leaves the fixture of the application under test to attach the content
    ///     by hand, per operation name.
    /// </remarks>
    [Fact]
    public void ATransitionThatTakesABody_PostsOne()
    {
        var source = Render(Transition(true) with
        {
            TransitionFields = new EquatableArray<CrudFieldModel>(
            [
                new CrudFieldModel { Name = "Reference", ValueExpression = "\"PAY-1\"" }
            ])
        });

        source.Should().Contain("Reference = \"PAY-1\",",
            "the body is synthesised from the endpoint's own members");
        source.Should().Contain(
            "transitionRequest.Content = global::System.Net.Http.Json.JsonContent.Create("
            + "global::Pragmatic.Testing.PragmaticContractHost.Body(\"Invoice_TransitionToPaid\", transitionBody));",
            "and it travels under the transition's own operation name, so the application can replace it");
    }

    /// <summary>
    ///     The control: a transition that takes nothing but its route still posts no content.
    /// </summary>
    /// <remarks>
    ///     Without it, "a transition posts its body" is satisfied by always attaching one — and an empty
    ///     JSON object posted to an endpoint with no body-bound member is a shape nobody sends, which is
    ///     the opposite defect. This is also what makes the change additive: every transition in the
    ///     repository that only needs its route keeps the request it has today.
    /// </remarks>
    [Fact]
    public void ATransitionThatTakesNoBody_PostsNoContent()
    {
        var source = Render(Transition(true));

        source.Should().NotContain("transitionRequest.Content",
            "an endpoint with no body-bound member takes no body");
        source.Should().NotContain("transitionBody",
            "and nothing is synthesised for it");
        source.Should().Contain("createRequest.Content = ",
            "while the create that arranges the initial state still posts its own");
    }

    /// <summary>
    ///     A transition whose required member the synthesiser cannot invent asks the application, under
    ///     the transition's own operation name.
    /// </summary>
    /// <remarks>
    ///     The null body is deliberate: <c>PragmaticContractHost.Body</c> throws naming the operation and
    ///     <c>BodyFor</c>, which is a failure that says what to do. Posting <c>{}</c> instead would be a
    ///     400 the contract would report as a broken endpoint.
    /// </remarks>
    [Fact]
    public void ATransitionBodyTheShapeCannotFill_IsAskedOfTheApplication()
    {
        var source = Render(Transition(true) with { CanSynthesizeTransitionBody = false });

        source.Should().Contain("object? transitionBody = null;",
            "nothing could be filled, and Body is what says so by name");
        source.Should().Contain(
            "global::Pragmatic.Testing.PragmaticContractHost.Body(\"Invoice_TransitionToPaid\", transitionBody)",
            "the application supplies it through BodyFor under that name");
    }

    [Fact]
    public void GeneratesClassAndBase()
    {
        Render(Transition(true)).Should().Contain(
            "public sealed partial class BillingTransitionContractTests : global::Pragmatic.Testing.PragmaticContractTestBase");
    }

    [Fact]
    public void LegalInitialTransition_GeneratesValidTest_WithCreateThenTransition()
    {
        var source = Render(Transition(initialIsLegal: true));

        source.Should().Contain("public async global::System.Threading.Tasks.Task Invoice_TransitionToPaid_FromInitial_Succeeds()");
        source.Should().Contain("new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Post, \"/api/invoices\");");
        source.Should().Contain("Amount = 1.0m,");
        source.Should().Contain("var transitionUrl = \"/api/invoices/{id}/pay\".Replace(\"{id}\", id);");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeSuccess(transitionResponse);");
    }

    /// <summary>
    ///     Issuing is legal from Draft; the contract's other half issues once, then issues again from Issued
    ///     — a state Issued may not be entered from — and expects 409.
    /// </summary>
    [Fact]
    public void LegalInitialTransition_WalksToAnIllegalSource_ThenExpectsConflict()
    {
        var issue = Issue();
        var source = new StateTransitionTestTemplate("Billing", [issue]).RenderOutput().Text;

        var half = Method(source, "Invoice_TransitionToIssued_FromIllegalState_IsRejectedWithConflict");
        half.Should().Contain("var step1Url = \"/api/invoices/{id}/issue\".Replace(\"{id}\", id);");
        half.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeSuccess(step1Response);");
        half.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeConflict(transitionResponse);");
    }

    /// <summary>
    ///     Paying is illegal from Draft; the contract's other half issues first — the walk the declarations
    ///     describe — then pays, and expects success.
    /// </summary>
    [Fact]
    public void IllegalInitialTransition_WalksToALegalSource_ThenExpectsSuccess()
    {
        var pay = Transition(initialIsLegal: false) with { LegalSources = new EquatableArray<string>(["Issued"]) };
        var source = new StateTransitionTestTemplate("Billing", [Issue(), pay]).RenderOutput().Text;

        var half = Method(source, "Invoice_TransitionToPaid_FromLegalState_Succeeds");
        half.Should().Contain("global::Pragmatic.Testing.PragmaticContractHost.Prepare(step1Request, \"Invoice_TransitionToIssued\", Boundary);");
        half.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeSuccess(transitionResponse);");
    }

    /// <summary>No transition leads to a legal source: no test for that half, and no skipped placeholder either.</summary>
    [Fact]
    public void NoWalk_NoTestAndNoPlaceholder()
    {
        var pay = Transition(initialIsLegal: false) with { LegalSources = new EquatableArray<string>(["Issued"]) };
        var source = new StateTransitionTestTemplate("Billing", [pay]).RenderOutput().Text;

        source.Should().NotContain("FromLegalState_Succeeds");
        source.Should().NotContain("Skip");
    }

    private static StateTransitionModel Issue() => Transition(initialIsLegal: true) with
    {
        TransitionRoute = "/api/invoices/{id}/issue",
        TargetState = "Issued",
        LegalSources = new EquatableArray<string>(["Draft"]),
    };

    /// <summary>The text of one generated test method, from its signature to the next test's attribute.</summary>
    private static string Method(string source, string name)
    {
        var start = source.IndexOf($" {name}()", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the generated class has no test named {name}");
        var end = source.IndexOf("[global::Xunit.FactAttribute]", start, System.StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    [Fact]
    public void IllegalInitialTransition_GeneratesConflictTest()
    {
        var source = Render(Transition(initialIsLegal: false));

        source.Should().Contain("public async global::System.Threading.Tasks.Task Invoice_TransitionToPaid_FromInitial_IsRejectedWithConflict()");
        source.Should().Contain("global::Pragmatic.Testing.PragmaticHttpAssertions.ShouldBeConflict(transitionResponse);");
    }

}
