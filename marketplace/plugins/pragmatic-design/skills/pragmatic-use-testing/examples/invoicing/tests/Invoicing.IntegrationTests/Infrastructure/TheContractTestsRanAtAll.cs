using System.Reflection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     The contract tests the Testing generator writes for this application exist, and the one this story
///     is about is among them.
/// </summary>
/// <remarks>
///     A generated suite that emits nothing is indistinguishable from one that passes — <c>Validate()</c>
///     returning false is all it takes, and nobody notices a class that is not there. This asserts the
///     classes were discovered and names the tests that matter.
/// </remarks>
public sealed class TheContractTestsRanAtAll
{
    /// <summary>
    ///     The generated contract-test classes: the ones deriving from the harness's base, not everything
    ///     the generator puts in that namespace — it also emits a typed <c>Api</c> client there.
    /// </summary>
    private static readonly IReadOnlyList<Type> Generated =
        [.. typeof(TheContractTestsRanAtAll).Assembly.GetTypes()
            .Where(type => typeof(Pragmatic.Testing.PragmaticContractTestBase).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    [Fact]
    public void TheGeneratorEmittedAClassPerBoundary()
        => Generated.Select(type => type.Name).Should().BeEquivalentTo(new[]
        {
            "BillingAuthContractTests",
            "BillingCrudContractTests",
            "BillingTransitionContractTests",
            "RegistryAuthContractTests",
            "RegistryCrudContractTests",
        });

    /// <summary>
    ///     The invoice's state machine, which is the richest of the three examples — it has a move back,
    ///     <c>Paid → Issued</c> — has a generated contract for each of its three moves.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         None of the three operations that perform them declared <c>[TransitionsTo]</c>, and
    ///         that attribute is the only thing that makes a transition readable across assembly metadata —
    ///         the target state lives in the body's <c>entity.TransitionTo(...)</c>, which the contract-test
    ///         generator cannot see. So <c>StateTransitionExtractor</c> returned null for all three and
    ///         <b>no transition contract existed at all</b>, with nothing to notice: the machine looked
    ///         covered because nobody was looking.
    ///     </para>
    ///     <para>
    ///         From a fresh draft only <c>Issued</c> is reachable in one POST, so that move asserts the
    ///         success and the other two assert the 409 — each with the opposite case emitted as a named
    ///         skip, because reaching it needs a multi-step flow.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheInvoicesStateMachine_HasAContractPerMove()
        => FactsOf("BillingTransitionContractTests").Should().BeEquivalentTo(new[]
        {
            "Invoice_TransitionToIssued_FromInitial_Succeeds",
            "Invoice_TransitionToIssued_FromIllegalState_IsRejectedWithConflict",
            "Invoice_TransitionToPaid_FromInitial_IsRejectedWithConflict",
            "Invoice_TransitionToPaid_FromLegalState_Succeeds",
            "Invoice_TransitionToVoid_FromInitial_IsRejectedWithConflict",
            "Invoice_TransitionToVoid_FromLegalState_Succeeds",
        });

    /// <summary>
    ///     The test the multi-tenancy skill calls the proof of isolation: it exists, for the entity whose
    ///     create the generator can fill from its shape.
    /// </summary>
    [Fact]
    public void TheTenantIsolationContract_IsEmittedForTheCustomer()
        => FactsOf("RegistryCrudContractTests")
            .Should().Contain("CreateCreateCustomerMutation_IsNotVisibleToAnotherTenant");

    /// <summary>
    ///     And for the invoice, whose create the generator <b>cannot</b> fill from its shape: it carries a
    ///     customer id and a nested collection of line mutations.
    /// </summary>
    /// <remarks>
    ///     The generator emits the create and the isolation test even for a body it cannot synthesise,
    ///     and the <c>BodyFor</c> hook is how the application supplies that body. An aggregate whose
    ///     create carries its children is most aggregates, so without both halves the isolation proof
    ///     would be missing for most of them — and present only for the flat ones.
    /// </remarks>
    [Fact]
    public void TheTenantIsolationContract_IsEmittedForTheInvoice_WithABodyFromTheApplication()
        => FactsOf("BillingCrudContractTests").Should().BeEquivalentTo(new[]
        {
            "CreateCreateDraftInvoiceMutation_WithValidBody_IsCreated",
            "CreateCreateDraftInvoiceMutation_WithMissingRequiredFields_IsRejected",
            "CreateCreateDraftInvoiceMutation_IsNotVisibleToAnotherTenant",
        });

    private static IReadOnlyList<string> FactsOf(string className)
        => [.. Generated.Single(type => type.Name == className)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute"))
            .Select(method => method.Name)];
}
