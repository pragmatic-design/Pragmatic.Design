using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     A composite whose steps are all actions, with no mutation among them.
/// </summary>
/// <remarks>
///     <para>
///         <c>CompositeStepKind</c> has three values — <c>Mutation</c>, <c>Action</c>, <c>VoidAction</c> —
///         so a composite made only of actions is a shape the framework accepts. The outer invoker,
///         though, decided everything about composites from the <b>mutation</b> step-invoker list being
///         non-empty: whether to hold a <c>CompositeInvoker</c>, whether to declare
///         <c>CommitsItsOwnWork</c>, whether to register the invoker at all, and whether it may open the
///         transaction itself. All of them read "not a composite" when no step happened to be a mutation,
///         while a doc comment two lines away already claimed the flag covered every step kind.
///     </para>
///     <para>
///         The transaction is the sharp end. Two invokers calling <c>BeginTransactionAsync</c> on one
///         connection is what EF Core refuses at run time — the defect a consumer application already hit
///         once, fixed then only for composites that do have mutation steps.
///     </para>
/// </remarks>
public class CompositeWithActionStepsTests : ActionsGeneratorTestBase
{
    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Commit;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Billing;

        [Boundary]
        public partial class BillingBoundary;

        public partial class Payment : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [DomainAction]
        public partial class ChargeCardAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        [DomainAction]
        public partial class SendReceiptAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        [DomainAction]
        [CompositeAction]
        [Transactional]
        [BelongsTo<BillingBoundary>]
        public partial class SettlePaymentComposite : VoidDomainAction
        {
            public required ChargeCardAction Charge { get; init; }
            public required SendReceiptAction Receipt { get; init; }
        }
        """;

    /// <summary>The composite pipeline is generated for action steps just as it is for mutations.</summary>
    [Fact]
    public void ACompositeOfActions_StillGetsItsCompositeInvoker()
    {
        var generated = GetGeneratedSource(RunGeneratorWithEntities(Source), "CompositeInvoker");

        generated.Should().NotBeNull("CompositeStepKind.Action is a step kind, so the pipeline exists");
    }

    /// <summary>And the outer invoker has to actually reach it.</summary>
    /// <remarks>
    ///     Without the field there is no call: the composite's own pipeline is generated and then left
    ///     with no caller, which is the failure mode that does not announce itself.
    /// </remarks>
    [Fact]
    public void TheOuterInvoker_HoldsTheCompositeInvoker()
    {
        var generated = GetGeneratedSource(RunGeneratorWithEntities(Source), "SettlePaymentComposite.Invoker");

        generated.Should().Contain("_compositeInvoker",
            "a composite delegates its steps, whatever kind they are");
    }

    /// <summary>
    ///     And it must not claim the transaction the composite pipeline already opens.
    /// </summary>
    /// <remarks>
    ///     This is the one that fails loudly: EF Core answers "the connection is already in a
    ///     transaction" at the second <c>BeginTransactionAsync</c>.
    /// </remarks>
    [Fact]
    public void TheOuterInvoker_DoesNotAlsoOpenTheTransaction()
    {
        var generated = GetGeneratedSource(RunGeneratorWithEntities(Source), "SettlePaymentComposite.Invoker");

        generated.Should().NotContain("protected override bool IsTransactional => true;",
            "the composite invoker opens it, and two on one connection is what EF Core refuses");
    }

    /// <summary>The commit belongs to the composite pipeline, not to the outer one.</summary>
    [Fact]
    public void TheOuterInvoker_LeavesTheCommitToTheComposite()
    {
        var generated = GetGeneratedSource(RunGeneratorWithEntities(Source), "SettlePaymentComposite.Invoker");

        generated.Should().Contain("CommitsItsOwnWork",
            "otherwise the outer pipeline saves a second time, on top of the composite's commit");
    }
}
