using Xunit;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     The parts of a contract request only the application knows come from the application.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The generator fills a request from the operation's <b>shape</b> and invents an identity
///         carrying a permission. Neither is always enough: a create whose validity needs a value that
///         must already exist is refused with 400, and a tenancy operation, or a host that derives
///         permissions from roles, refuses with 403. Both are the application behaving correctly, and
///         the generated test failed on its own assumption.
///     </para>
///     <para>
///         So the seam is the one the consumer's fixture already writes to. No interface to implement,
///         no partial to declare: two delegates beside the client.
///     </para>
/// </remarks>
[Collection(TheProcessWideContractHostCollection.Name)]
public sealed class TheApplicationHasTheLastWordTests : IDisposable
{
    public TheApplicationHasTheLastWordTests() => PragmaticContractHost.Reset();

    public void Dispose()
    {
        PragmaticContractHost.Reset();
        GC.SuppressFinalize(this);
    }

    /// <summary>A body the application supplies replaces the synthesised one.</summary>
    [Fact]
    public void ABodyTheApplicationSupplies_ReplacesTheSynthesisedOne()
    {
        var supplied = new { Name = "from the application" };
        PragmaticContractHost.BodyFor = op => op == "CreateWorkspace" ? supplied : null;

        PragmaticContractHost.Body("CreateWorkspace", new { Name = "synthesised" })
            .Should().BeSameAs(supplied);
    }

    /// <summary>
    ///     The control: an operation the application says nothing about keeps the synthesised body.
    /// </summary>
    /// <remarks>
    ///     Most creates are carried by their shape. Without this, "the application supplies the body"
    ///     would be satisfied by a hook that made every consumer write one for every create.
    /// </remarks>
    [Fact]
    public void AnOperationTheApplicationIgnores_KeepsTheSynthesisedBody()
    {
        var synthesised = new { Name = "synthesised" };
        PragmaticContractHost.BodyFor = _ => null;

        PragmaticContractHost.Body("CreateSomethingElse", synthesised).Should().BeSameAs(synthesised);
    }

    /// <summary>And with no hook at all, nothing changes.</summary>
    [Fact]
    public void WithNoHook_TheSynthesisedBodyIsUsed()
    {
        var synthesised = new { Name = "synthesised" };

        PragmaticContractHost.Body("CreateAnything", synthesised).Should().BeSameAs(synthesised);
    }

    /// <summary>
    ///     A create the generator cannot fill from its shape now gets its tests anyway, and
    ///     asks the application for a body. With nobody answering, it says so: the operation's name, and
    ///     where to write it.
    /// </summary>
    /// <remarks>
    ///     The alternative was what happened before: the generator emitted neither the create nor the
    ///     tenant-isolation test, silently, and the hook that exists for exactly this case was read inside
    ///     the tests that were missing. A suite that emits one test where it could emit three looks
    ///     exactly like a suite that passes.
    /// </remarks>
    [Fact]
    public void ACreateNobodyCanFill_SaysWhoHasToFillIt()
    {
        var thrown = Record.Exception(() => PragmaticContractHost.Body("CreateDraftInvoiceMutation", null));

        thrown.Should().BeOfType<InvalidOperationException>();
        thrown!.Message.Should().Contain("CreateDraftInvoiceMutation", "the test says which create it is")
            .And.Contain(nameof(PragmaticContractHost.BodyFor), "and where the body comes from");
    }

    /// <summary>The control: the application answering is the ordinary case, and it is not an error.</summary>
    [Fact]
    public void ACreateTheApplicationFillsIn_IsNoError()
    {
        var supplied = new { CustomerId = Guid.NewGuid() };
        PragmaticContractHost.BodyFor = _ => supplied;

        PragmaticContractHost.Body("CreateDraftInvoiceMutation", null).Should().BeSameAs(supplied);
    }

    /// <summary>The application gets the request last, and may add what the generator cannot know.</summary>
    /// <remarks>
    ///     A tenant header, a role claim: host wiring, not module metadata.
    /// </remarks>
    [Fact]
    public void TheApplication_GetsTheRequestLast()
    {
        PragmaticContractHost.PrepareRequest = contract =>
            contract.Message.Headers.Add("X-Tenant-Id", $"tenant-for-{contract.Operation}");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://app/api/workspaces");

        PragmaticContractHost.Prepare(request, "CreateWorkspace", "Workspaces")
            .Headers.GetValues("X-Tenant-Id").Should().BeEquivalentTo(["tenant-for-CreateWorkspace"]);
    }

    /// <summary>The control: without a hook the request is handed back untouched.</summary>
    [Fact]
    public void WithNoHook_TheRequestIsUntouched()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://app/api/workspaces");

        PragmaticContractHost.Prepare(request, "CreateWorkspace", "Workspaces").Should().BeSameAs(request);
        request.Headers.Should().BeEmpty();
    }
}
