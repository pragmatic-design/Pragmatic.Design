using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[StartsDelegation]</c>: an action that runs on behalf of someone else.
/// </summary>
/// <remarks>
///     <para>
///         The scope is generated rather than left to the author for two reasons the tests below hold
///         to. The action body runs <em>after</em> authorization has decided, so a hand-written
///         <c>ActAs</c> there would authorize as the caller and execute as the subject — the fail-open
///         the attribute exists to prevent. And a <c>using</c> forgotten on an <c>AsyncLocal</c> leaks
///         into the next request on the same pooled thread, with nothing to report it.
///     </para>
///     <para>
///         What the framework still does not answer is <em>who may act for whom</em>: with no grant
///         store, the action's own permission is the only gate. That is a documented limit, not
///         something these tests can close.
///     </para>
///     <para>
///         ⚠️ <b>These tests read generated text and nothing else</b>, so they cannot see when the scope
///         closes: every one of them passes with the scope closing before the row is written. The
///         effect is measured in <c>ADelegationCoversTheWriteItAttributesTests</c>, and the two
///         halves belong together — a snapshot of the right call in the wrong place looks identical
///         to a snapshot of the right call in the right place.
///     </para>
/// </remarks>
public class StartsDelegationTests : ActionsGeneratorTestBase
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Identity;
        using Pragmatic.Result;
        """;

    private static string Source(string attribute, string subjectProperty = "public string ForMemberId { get; init; } = \"\";") => $$"""
        {{Usings}}

        namespace TestApp.Work;

        [Boundary]
        public partial class WorkBoundary;

        {{attribute}}
        [DomainAction]
        public partial class TakeOverWorkItemAction : DomainAction<Guid>
        {
            {{subjectProperty}}

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    [Fact]
    public void StartsDelegation_OpensAScopeOverTheNamedProperty()
    {
        var result = RunGenerator(Source("""[StartsDelegation(nameof(TakeOverWorkItemAction.ForMemberId), Purpose = "agent takeover")]"""));

        var invoker = GetGeneratedSource(result, "Invoker");

        invoker.Should().Contain("_delegationService?.ActAs(action.ForMemberId",
            "the subject is read off the action at invocation, not baked in");
        invoker.Should().Contain("agent takeover", "the purpose reaches the audit trail");
        invoker.Should().Contain("BeginInvocationScope",
            "the pipeline holds the scope across the save — see the remark on this class");
    }

    /// <summary>
    ///     The scope must be emitted for an action that declares nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An override is emitted only for actions that need it, and the conditions list what
    ///         needs one — composite, versioned, resilient, filter-overriding. A delegation-only
    ///         action matches none of those, so the whole attribute would compile and generate
    ///         nothing: the "declared but inert" shape.
    ///     </para>
    ///     <para>
    ///         ⚠️ The host is <c>BeginInvocationScope</c>, not <c>ExecuteActionAsync</c>, and the rule
    ///         this test pins is independent of it: the declaration must
    ///         reach the generated invoker on its own. A delegation-only action emits no execution
    ///         override, and must not: that override ends before the save.
    ///     </para>
    /// </remarks>
    [Fact]
    public void StartsDelegation_Alone_StillEmitsTheScope()
    {
        var result = RunGenerator(Source("""[StartsDelegation(nameof(TakeOverWorkItemAction.ForMemberId))]"""));

        var invoker = GetGeneratedSource(result, "Invoker");

        invoker.Should().Contain("BeginInvocationScope",
            "an action whose only contribution is the delegation still needs the override that opens it");
        invoker.Should().Contain("ActAs(action.ForMemberId");
    }

    /// <summary>
    ///     The scope does not live in the execution override, because that override returns
    ///     before the invoker saves — and the save is where the row is attributed.
    /// </summary>
    /// <remarks>
    ///     A text assertion, so it can only say where the scope is written. That it is still open when
    ///     the write lands is the runtime half, and it is measured in
    ///     <c>ADelegationCoversTheWriteItAttributesTests</c>.
    /// </remarks>
    [Fact]
    public void StartsDelegation_DoesNotWrapTheBodyAlone()
    {
        var result = RunGenerator(Source("""[StartsDelegation(nameof(TakeOverWorkItemAction.ForMemberId))]"""));

        GetGeneratedSource(result, "Invoker").Should().NotContain("ExecuteActionAsync",
            "a scope opened there has disposed by the time OwnershipInterceptor and "
            + "AuditingInterceptor stamp the row, which is the whole defect");
    }

    [Fact]
    public void StartsDelegation_DefaultsToIntersection()
    {
        var result = RunGenerator(Source("""[StartsDelegation(nameof(TakeOverWorkItemAction.ForMemberId))]"""));

        GetGeneratedSource(result, "Invoker").Should().Contain("DelegationPolicy)0",
            "the narrowest policy is the default: neither more than the caller nor more than the subject");
    }

    [Fact]
    public void StartsDelegation_CarriesAnExplicitPolicy()
    {
        var result = RunGenerator(Source(
            """[StartsDelegation(nameof(TakeOverWorkItemAction.ForMemberId), Policy = DelegationPolicy.GrantScoped)]"""));

        GetGeneratedSource(result, "Invoker").Should().Contain("DelegationPolicy)2");
    }

    /// <summary>
    ///     A subject that does not resolve is an error, not a silent no-op.
    /// </summary>
    /// <remarks>
    ///     Generating nothing would leave the action running as the caller while its declaration says
    ///     it acts for someone else — worse than not offering the attribute. Generating a scope over
    ///     the missing value would throw from <c>ActAs</c> at runtime, inside a domain action.
    /// </remarks>
    [Fact]
    public void StartsDelegation_WithAMissingProperty_ReportsPrag0423()
    {
        var result = RunGenerator(Source("""[StartsDelegation("NoSuchProperty")]"""));

        HasDiagnostic(result, "PRAG0423").Should().BeTrue();
    }

    [Fact]
    public void StartsDelegation_WithANonStringProperty_ReportsPrag0423()
    {
        var result = RunGenerator(
            Source("""[StartsDelegation("ForMemberId")]""", "public Guid ForMemberId { get; init; }"));

        HasDiagnostic(result, "PRAG0423").Should().BeTrue(
            "a Guid subject would compile and then fail at runtime, or silently stringify to something no identity matches");
    }

    [Fact]
    public void WithoutTheAttribute_NoDelegationIsGenerated()
    {
        var result = RunGenerator(Source(""));

        var invoker = GetGeneratedSource(result, "Invoker");

        invoker.Should().NotContain("__delegation");
        invoker.Should().NotContain("IDelegationService",
            "an application that never delegates must not gain a dependency on it");
    }
}
