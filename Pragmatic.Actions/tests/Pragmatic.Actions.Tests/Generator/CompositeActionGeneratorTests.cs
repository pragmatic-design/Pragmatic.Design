using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     End-to-end generator tests for <c>[CompositeAction]</c>.
///     A composite action declares mutation-typed properties as "steps"; the SG emits a nested
///     <c>CompositeInvoker</c> that executes each step without saving, then commits atomically
///     through a single <see cref="Pragmatic.Persistence.Repository.IUnitOfWork" />.
/// </summary>
/// <remarks>
///     These tests assert on the generated <c>CompositeInvoker</c> source via string matching
///     (the SG-gap convention). Full host compilation of the composite + its cross-referenced
///     mutation invokers requires a wired DbContext and is covered by Showcase E2E, not here.
/// </remarks>
public class CompositeActionGeneratorTests : ActionsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Commit;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;
        """;

    private const string VoidCompositeSource = CommonUsings + """

        namespace TestApp.Sales;

        public partial class Reservation : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [Mutation(Mode = MutationMode.Create)]
        public partial class CreateReservationMutation : Mutation<Reservation>
        {
            public required string RoomNumber { get; init; }
        }

        [DomainAction]
        [CompositeAction]
        public partial class PlaceOrderComposite : VoidDomainAction
        {
            public required CreateReservationMutation Reservation { get; init; }
        }
        """;

    [Fact]
    public void CompositeAction_GeneratesNestedCompositeInvoker()
    {
        var result = RunGeneratorWithEntities(VoidCompositeSource);

        var generated = GetGeneratedSource(result, "CompositeInvoker");
        generated.Should().NotBeNull();

        // Nested inside the partial action class.
        generated.Should().Contain("partial class PlaceOrderComposite");
        generated.Should().Contain("sealed class CompositeInvoker");
    }

    [Fact]
    public void CompositeAction_InjectsUnitOfWorkAndStepInvoker()
    {
        var result = RunGeneratorWithEntities(VoidCompositeSource);

        var generated = GetGeneratedSource(result, "CompositeInvoker");
        generated.Should().NotBeNull();

        // UnitOfWork field + the mutation step's invoker field (named from the property).
        generated.Should().Contain("global::Pragmatic.Persistence.Repository.IUnitOfWork _unitOfWork");
        generated.Should().Contain("_reservationInvoker");
        // The step invoker is the NESTED concrete invoker (not a non-existent top-level type).
        generated.Should().Contain("global::TestApp.Sales.CreateReservationMutation.Invoker");
    }

    [Fact]
    public void CompositeAction_ExecutesStepWithoutSaveThenCommitsOnce()
    {
        var result = RunGeneratorWithEntities(VoidCompositeSource);

        var generated = GetGeneratedSource(result, "CompositeInvoker");
        generated.Should().NotBeNull();

        // Each step runs without saving, failures short-circuit, then a single SaveChanges commits.
        // InvokeAsync, not a special entry point: the composite claims the unit of work first, so an
        // ordinary invocation already stages. That unification is what let action steps exist at all.
        generated.Should().Contain("InvokeAsync(action.Reservation, ct)");
        generated.Should().Contain("IsFailure");
        generated.Should().Contain("_unitOfWork.SaveChangesAsync(ct)");

        // One batch for all the steps, and the events flushed after the commit — otherwise they are
        // dropped when the scope ends. The batch is the one CommitScope opens for this unit of work,
        // not an ambient one of the composite's own, which would cover every boundary including those
        // it could not save.
        generated.Should().Contain("CommitScope.Claim");
        generated.Should().Contain("global::Pragmatic.Events.IDomainEventDispatcher");
        generated.Should().Contain("__batch.DeferredEvents");
    }

    [Fact]
    public void CompositeAction_DelegatingInvoker_DelegatesToCompositeInvoker()
    {
        var result = RunGeneratorWithEntities(VoidCompositeSource);

        // The action's own Invoker (which runs filters/permissions) delegates step orchestration
        // to the CompositeInvoker rather than calling action.Execute directly.
        var invoker = GetGeneratedSource(result, "PlaceOrderComposite.Invoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("CompositeInvoker compositeInvoker");
        invoker.Should().Contain("_compositeInvoker.ExecuteAsync(action, ct)");
    }

    [Fact]
    public void CompositeAction_VoidReturn_ReturnsVoidResultSuccess()
    {
        var result = RunGeneratorWithEntities(VoidCompositeSource);

        var generated = GetGeneratedSource(result, "CompositeInvoker");
        generated.Should().NotBeNull();

        generated.Should().Contain(
            "global::Pragmatic.Result.VoidResult<global::Pragmatic.Result.IError>.Success()");
    }

    [Fact]
    public void CompositeAction_WithBelongsTo_KeysUnitOfWorkToBoundary()
    {
        var source = CommonUsings + """

            namespace TestApp.Sales;

            [Pragmatic.Actions.Attributes.Boundary]
            public partial class SalesBoundary;

            public partial class Reservation : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            [Mutation(Mode = MutationMode.Create)]
            public partial class CreateReservationMutation : Mutation<Reservation>
            {
                public required string RoomNumber { get; init; }
            }

            [DomainAction]
            [CompositeAction]
            [BelongsTo<SalesBoundary>]
            public partial class PlaceOrderComposite : VoidDomainAction
            {
                public required CreateReservationMutation Reservation { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        var result = RunGeneratorWithEntities(source);

        var generated = GetGeneratedSource(result, "CompositeInvoker");
        generated.Should().NotBeNull();

        // The UnitOfWork is resolved as a keyed service bound to the boundary.
        generated.Should().Contain("FromKeyedServices(typeof(global::TestApp.Sales.SalesBoundary))");
    }

    // =========================================================================
    // Action steps — the half that was missing
    // =========================================================================

    private const string ActionStepsSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Sales;

        [Boundary]
        public partial class SalesBoundary;

        [DomainAction]
        public partial class ArchiveOrderAction : DomainAction<Guid>
        {
            public required Guid OrderId { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(OrderId));
        }

        [DomainAction]
        public partial class NotifyClosureAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        [DomainAction]
        [CompositeAction]
        public partial class CloseAccountAction : VoidDomainAction
        {
            public required ArchiveOrderAction Archive { get; init; }
            public required NotifyClosureAction Notify { get; init; }

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    /// <summary>
    ///     A composite of actions generates an invoker. Before this it generated nothing at all, and
    ///     because the convention leaves the body empty, the action did nothing — without a diagnostic.
    /// </summary>
    [Fact]
    public void CompositeOfActions_GeneratesTheInvoker()
    {
        var result = RunGenerator(ActionStepsSource);

        var invoker = GetGeneratedSource(result, "CloseAccountAction.CompositeInvoker");

        invoker.Should().NotBeNull("a composite of action steps must orchestrate them");
        invoker.Should().Contain("_archiveInvoker.InvokeAsync(action.Archive");
        invoker.Should().Contain("_notifyInvoker.InvokeAsync(action.Notify");
    }

    /// <summary>
    ///     Actions are injected by interface, not by the nested Invoker: a step may live in another
    ///     assembly of the same boundary, and the registration is by interface anyway.
    /// </summary>
    [Fact]
    public void CompositeOfActions_InjectsTheInvokerInterfaces()
    {
        var invoker = GetGeneratedSource(RunGenerator(ActionStepsSource), "CloseAccountAction.CompositeInvoker")!;

        invoker.Should().Contain("IDomainActionInvoker<global::TestApp.Sales.ArchiveOrderAction, global::System.Guid>");
        invoker.Should().Contain("IVoidDomainActionInvoker<global::TestApp.Sales.NotifyClosureAction>");
    }

    /// <summary>
    ///     One transaction: the composite claims the unit of work, so each step stages instead of
    ///     committing, and the commit happens once at the end.
    /// </summary>
    [Fact]
    public void CompositeOfActions_ClaimsTheUnitOfWorkBeforeTheSteps()
    {
        var invoker = GetGeneratedSource(RunGenerator(ActionStepsSource), "CloseAccountAction.CompositeInvoker")!;

        var claim = invoker.IndexOf("CommitScope.Claim", StringComparison.Ordinal);
        var firstStep = invoker.IndexOf("_archiveInvoker.InvokeAsync", StringComparison.Ordinal);

        claim.Should().BeGreaterThan(-1, "without the claim each step would commit on its own");
        firstStep.Should().BeGreaterThan(claim, "the claim has to be in place before the first step runs");
    }

    [Fact]
    public void CompositeWithNoSteps_ReportsPrag0427()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Sales;

            [Boundary]
            public partial class SalesBoundary;

            [DomainAction]
            [CompositeAction]
            public partial class OrchestratesNothingAction : VoidDomainAction
            {
                public required string Name { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        HasDiagnostic(RunGenerator(source), "PRAG0427").Should().BeTrue(
            "no steps means no generated invoker, and with the conventional empty body, no behaviour");
    }

    [Fact]
    public void CompositeWithSteps_ReportsNoPrag0427()
        => HasDiagnostic(RunGenerator(ActionStepsSource), "PRAG0427").Should().BeFalse();

    // =========================================================================
    // [Transactional] on a composite
    // =========================================================================

    private static string CompositeWith(string attributes) =>
        VoidCompositeSource.Replace("[CompositeAction]", "[CompositeAction]" + Environment.NewLine + attributes);

    /// <summary>
    ///     A composite commits once, which is what a composite is — so it opens no transaction, and the
    ///     single <c>SaveChanges</c> is already atomic.
    /// </summary>
    [Fact]
    public void APlainComposite_OpensNoTransaction()
    {
        GetGeneratedSource(RunGeneratorWithEntities(VoidCompositeSource), "CompositeInvoker")
            .Should().NotContain("BeginTransactionAsync");
    }

    /// <summary>
    ///     <c>[Transactional]</c> buys the one thing a composite cannot: a step that reads what an
    ///     earlier step wrote. It was parsed by nobody and the template hardcoded <c>Once</c>, so the
    ///     attribute read as a guarantee and did nothing.
    /// </summary>
    [Fact]
    public void ATransactionalComposite_OpensAndCommitsOne()
    {
        var generated = GetGeneratedSource(
            RunGeneratorWithEntities(CompositeWith("[Transactional]")), "CompositeInvoker");

        generated.Should().Contain("BeginTransactionAsync", "the transaction has to be opened");
        generated.Should().Contain("CommitAsync", "and committed after the save");
        generated.Should().Contain("transactional: true",
            "the steps must save as they go, or a later step cannot read what an earlier one wrote");
    }

    /// <summary>
    ///     And rolled back explicitly on a failing step, rather than left to the dispose.
    /// </summary>
    [Fact]
    public void ATransactionalComposite_RollsBackAFailingStep()
    {
        GetGeneratedSource(RunGeneratorWithEntities(CompositeWith("[Transactional]")), "CompositeInvoker")
            .Should().Contain("RollbackAsync");
    }

    /// <summary>
    ///     PerStep asks a composite to be what a composite is not, and the invoker cannot honour it.
    /// </summary>
    [Fact]
    public void PerStepOnAComposite_IsReported()
    {
        HasDiagnostic(
            RunGeneratorWithEntities(CompositeWith("[CommitStrategy(CommitMode.PerStep)]")),
            "PRAG0430").Should().BeTrue();
    }

    [Fact]
    public void APlainComposite_IsNotReported()
    {
        HasDiagnostic(RunGeneratorWithEntities(VoidCompositeSource), "PRAG0430").Should().BeFalse();
    }

    /// <summary>
    ///     Exactly one invoker opens the transaction, and it is the composite's.
    /// </summary>
    /// <remarks>
    ///     A composite passes through two generated invokers: the ordinary one, which delegates the
    ///     unit of work to the nested <c>CompositeInvoker</c>, and the composite invoker itself. Both
    ///     read <c>[Transactional]</c>, so both called <c>BeginTransactionAsync</c> on the same
    ///     connection and EF Core refused the second — a 500 at run time, on the first real use.
    /// </remarks>
    [Fact]
    public void ATransactionalComposite_OpensTheTransactionOnlyOnce()
    {
        var sources = GetGeneratedSourcesAsDictionary(
            RunGeneratorWithEntities(CompositeWith("[Transactional]")));

        var outer = sources.First(kv => kv.Key.Contains("PlaceOrderComposite.Invoker")).Value;
        outer.Should().Contain("CommitsItsOwnWork => true",
            "the outer invoker hands the whole unit of work to the composite one");
        outer.Should().NotContain("IsTransactional => true",
            "and the transaction is part of that unit of work, so the outer must not open one too");

        sources.First(kv => kv.Key.Contains("CompositeInvoker")).Value
            .Should().Contain("BeginTransactionAsync", "the composite is the one that opens it");
    }

    /// <summary>
    ///     A non-composite action keeps declaring it: the guard must not have switched off the feature.
    /// </summary>
    [Fact]
    public void ATransactionalActionThatIsNotAComposite_StillDeclaresIt()
    {
        var source = CommonUsings + """

            namespace TestApp.Sales;

            [Boundary]
            public partial class SalesBoundary;

            public partial class Ticket : IEntity
            {
                public Guid PersistenceId { get; set; }
            }

            [DomainAction]
            [Transactional]
            [global::Pragmatic.Persistence.Entity.BelongsTo<SalesBoundary>]
            public partial class CloseTicketAction : VoidDomainAction
            {
                private global::Pragmatic.Persistence.Repository.IRepository<Ticket> _tickets = null!;

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        GetGeneratedSource(RunGeneratorWithEntities(source), "CloseTicketAction.Invoker")
            .Should().Contain("IsTransactional => true");
    }

    // =========================================================================
    // The Execute a composite has no use for
    // =========================================================================

    /// <summary>
    ///     A composite declares its steps and nothing else; the generator satisfies the base class.
    /// </summary>
    /// <remarks>
    ///     The base declares <c>Execute</c> abstract, and a composite's body <b>is</b> its steps — so
    ///     an author-written <c>=&gt; Task.FromResult(Success())</c> would exist just to compile: a method
    ///     in their own file stating that the operation does nothing and succeeds, which is false about
    ///     the first thing anyone reads. The generated one throws instead, because being called would mean
    ///     something had gone wrong.
    /// </remarks>
    [Fact]
    public void ACompositeNeedNotWriteAnExecute()
    {
        var result = RunGeneratorWithEntities(VoidCompositeSource);

        // CS0534, not "no errors": this harness does not reference everything an action needs, so it
        // always has some. Not implementing the base's abstract Execute has one code, and it is this.
        GetCompilationErrors(result).Select(d => d.Id).Should().NotContain("CS0534",
            "the source declares no Execute at all, and the base class demands one");

        GetGeneratedSource(result, "CompositeInvoker")
            .Should().Contain("public override global::System.Threading.Tasks.Task")
            .And.Contain("NotSupportedException");
    }

    /// <summary>
    ///     And an author who writes one anyway keeps it: two would collide, and writing a body on a
    ///     composite says something the generator should not overwrite.
    /// </summary>
    [Fact]
    public void AnAuthorWhoWritesOne_KeepsIt()
    {
        var source = VoidCompositeSource.Replace(
            "public required CreateReservationMutation Reservation { get; init; }",
            """
            public required CreateReservationMutation Reservation { get; init; }

                        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                            => Task.FromResult(VoidResult<IError>.Success());
            """);

        var result = RunGeneratorWithEntities(source);

        GetCompilationErrors(result).Select(d => d.Id).Should().NotContain("CS0111",
            "two Execute overrides in one partial class is a duplicate member");
        GetGeneratedSource(result, "CompositeInvoker").Should().NotContain("NotSupportedException");
    }
}
