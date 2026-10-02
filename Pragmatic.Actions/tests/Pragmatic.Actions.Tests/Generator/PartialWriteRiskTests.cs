using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     PRAG0424: an action that commits into more than one boundary in a single invocation.
///     <para>
///         Each boundary owns a <c>DbContext</c>, and <c>DomainActionInvoker</c> saves it once, at the
///         end, only when the result is a success. Call another boundary's action from inside yours and
///         there are two saves, the inner one first — so a failure after that point leaves the inner
///         writes committed against a parent row that was never written. Measured on a real
///         application: five orphan rows survived a deliberate failure.
///     </para>
///     <para>
///         The generator cannot know whether that matters: sometimes the leftovers are harmless,
///         sometimes they are the defect. What it can see is the shape, and what it insists on is that
///         the decision be recorded rather than left implicit.
///     </para>
/// </summary>
public class PartialWriteRiskTests : ActionsGeneratorTestBase
{
    /// <summary>
    ///     The facade is declared by hand here, carrying the marker the generator emits on the real one.
    ///     That is not a shortcut: a facade the generator produces in <i>this</i> compilation is
    ///     invisible to it, so the case the diagnostic exists for is always a referenced assembly — which
    ///     is exactly what a hand-written, attributed interface stands in for.
    /// </summary>
    private static string Source(
        string actionAttributes = "",
        bool ownRepository = true,
        bool foreignFacade = true,
        bool secondForeignFacade = false,
        bool ownBoundaryFacade = false,
        bool foreignStepCompensable = false) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Commit;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;

        namespace Other.Knowledge
        {
            public partial class KnowledgeBoundary;

            [BoundaryActions<KnowledgeBoundary>]
            public interface IKnowledgeActions
            {
        {{(foreignStepCompensable ? "        [CompensableStep]" : "")}}
                Task<VoidResult<IError>> Ingest(string text, CancellationToken ct = default);
            }
        }

        namespace Other.Billing
        {
            public partial class BillingBoundary;

            [BoundaryActions<BillingBoundary>]
            public interface IBillingActions
            {
                Task<VoidResult<IError>> Charge(decimal amount, CancellationToken ct = default);
            }
        }

        namespace TestApp.Work
        {
            [Boundary]
            public partial class WorkBoundary;

            // Named unlike the generated I{Boundary}Actions on purpose: the generator emits one for
            // WorkBoundary too, and two types of that name in one namespace is a CS0101 that hid here
            // until a test finally asserted the compilation instead of only the diagnostics.
            [BoundaryActions<WorkBoundary>]
            public interface IWorkOwnFacade
            {
                Task<VoidResult<IError>> Touch(CancellationToken ct = default);
            }

            public class WorkItem : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Title { get; set; } = "";
            }

            {{actionAttributes}}
            [DomainAction]
            [BelongsTo<WorkBoundary>]
            public partial class WriteStoryAction : VoidDomainAction
            {
        {{(ownRepository ? "        private IRepository<WorkItem> _items = null!;" : "")}}
        {{(foreignFacade ? "        private Other.Knowledge.IKnowledgeActions _knowledge = null!;" : "")}}
        {{(secondForeignFacade ? "        private Other.Billing.IBillingActions _billing = null!;" : "")}}
        {{(ownBoundaryFacade ? "        private IWorkOwnFacade _self = null!;" : "")}}

                public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                {
        {{(foreignFacade ? "            await _knowledge.Ingest(\"x\", ct);" : "")}}
        {{(secondForeignFacade ? "            await _billing.Charge(1m, ct);" : "")}}
        {{(ownBoundaryFacade ? "            await _self.Touch(ct);" : "")}}
                    return VoidResult<IError>.Success();
                }
            }
        }
        """;

    [Fact]
    public void OwnWrites_PlusForeignBoundaryCall_ReportsPrag0424()
    {
        var result = RunGeneratorWithEntities(Source());

        HasDiagnostic(result, "PRAG0424").Should().BeTrue(
            "the inner boundary commits before this one, and nothing rolls it back");
    }

    [Fact]
    public void OwnWrites_PlusForeignBoundaryCall_NamesTheFacadeInTheMessage()
    {
        var result = RunGeneratorWithEntities(Source());

        var message = GeneratorTestHelper
            .GetGeneratorDiagnostics(result, "PRAG0424")
            .Single()
            .GetMessage();

        message.Should().Contain("IKnowledgeActions",
            "naming the dependency is what makes the warning actionable");
    }

    [Fact]
    public void RecordedDecision_SilencesPrag0424()
    {
        var result = RunGeneratorWithEntities(
            Source(actionAttributes: """[AcceptsPartialWrites("The glossary prunes unreferenced candidates nightly.")]"""));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse(
            "the decision is recorded, which is the outcome the diagnostic asks for");
    }

    /// <summary>
    ///     One store is atomic by construction. Warning on every cross-boundary call would train the
    ///     reader to ignore the id.
    /// </summary>
    [Fact]
    public void ForeignBoundaryCall_WithNoWritesOfItsOwn_IsQuiet()
    {
        var result = RunGeneratorWithEntities(Source(ownRepository: false));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse();
    }

    [Fact]
    public void TwoForeignBoundaries_WithNoWritesOfItsOwn_ReportsPrag0424()
    {
        var result = RunGeneratorWithEntities(
            Source(ownRepository: false, secondForeignFacade: true));

        HasDiagnostic(result, "PRAG0424").Should().BeTrue(
            "the first boundary commits before the second one is even called");
    }

    /// <summary>Its own boundary's facade is its own store, not a second commit scope.</summary>
    [Fact]
    public void OwnBoundaryFacade_IsNotAForeignCommitScope()
    {
        var result = RunGeneratorWithEntities(
            Source(foreignFacade: false, ownBoundaryFacade: true));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse();
    }

    [Fact]
    public void OwnWritesOnly_IsQuiet()
    {
        var result = RunGeneratorWithEntities(Source(foreignFacade: false));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse();
    }

    /// <summary>
    ///     The step declares its own undo, so the caller's failure repairs it. Warning here would push
    ///     whoever did the work into [AcceptsPartialWrites], which would then say something false.
    /// </summary>
    [Fact]
    public void ForeignStepThatUndoesItself_IsQuiet()
    {
        var result = RunGeneratorWithEntities(Source(foreignStepCompensable: true));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse();
    }

    /// <summary>
    ///     One compensable step does not excuse the other: this action still writes its own rows and
    ///     still calls an unguarded step, which is two commit scopes.
    /// </summary>
    [Fact]
    public void OneStepCompensableAndAnotherNot_StillReportsPrag0424()
    {
        var result = RunGeneratorWithEntities(
            Source(secondForeignFacade: true, foreignStepCompensable: true));

        HasDiagnostic(result, "PRAG0424").Should().BeTrue();

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0424").Single().GetMessage()
            .Should().NotContain("Ingest", "the compensable step is not what is being reported");
    }

    /// <summary>
    ///     A facade held and never called commits nothing. Judging by the field would warn about a
    ///     dependency that does not write.
    /// </summary>
    [Fact]
    public void FacadeHeldButNeverCalled_IsQuiet()
    {
        var result = RunGeneratorWithEntities(
            Source().Replace("await _knowledge.Ingest(\"x\", ct);", ""));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse();
    }

    /// <summary>
    ///     Sub-boundary interfaces carry the marker too, and this is not tidiness: a caller writes
    ///     <c>_knowledge.Items.Ingest(…)</c>, so the invoked method belongs to the sub-boundary type.
    ///     Marking only the root facade made the diagnostic blind to every call through a sub-folder —
    ///     which is the normal shape of a boundary, not an edge case. Caught on a real application,
    ///     where the warning went quiet for the wrong reason.
    /// </summary>
    [Fact]
    public void SubBoundaryInterface_CarriesTheMarker()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Knowledge;

            [Boundary]
            public partial class KnowledgeBoundary;

            namespace TestApp.Knowledge.Items
            {
                // Surface, said out loud: with no [Endpoint] it would be inferred internal, and the
                // sub-boundary interface this test is about would have no member to carry the marker.
                [DomainAction(Internal = false)]
                public partial class IngestTextAction : VoidDomainAction
                {
                    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult(VoidResult<IError>.Success());
                }
            }
            """;

        var definition = GetGeneratedSource(RunGeneratorWithEntities(source), "Definition")!;

        var markers = definition.Split("[global::Pragmatic.Actions.Attributes.BoundaryActions<").Length - 1;

        markers.Should().BeGreaterThan(1,
            "the root facade and the sub-boundary interface both need it — a caller invokes the second");
    }

    /// <summary>
    ///     The call as a real application writes it: through the sub-boundary property, and awaited with
    ///     <c>ConfigureAwait</c>. Both were true of the case this diagnostic was built for.
    /// </summary>
    [Fact]
    public void CallThroughASubBoundaryProperty_ReportsPrag0424()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Repository;
            using Pragmatic.Result;

            namespace Other.Knowledge
            {
                public partial class KnowledgeBoundary;

                [BoundaryActions<KnowledgeBoundary>]
                public interface IKnowledgeItemsActions
                {
                    Task<VoidResult<IError>> Ingest(string text, CancellationToken ct = default);
                }

                [BoundaryActions<KnowledgeBoundary>]
                public interface IKnowledgeActions
                {
                    IKnowledgeItemsActions Items { get; }
                }
            }

            namespace TestApp.Work
            {
                [Boundary]
                public partial class WorkBoundary;

                public class WorkItem : IEntity
                {
                    public Guid PersistenceId { get; set; }
                }

                [DomainAction]
                [BelongsTo<WorkBoundary>]
                public partial class WriteStoryAction : VoidDomainAction
                {
                    private IRepository<WorkItem> _items = null!;
                    private Other.Knowledge.IKnowledgeActions _knowledge = null!;

                    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    {
                        await _knowledge.Items.Ingest("x", ct).ConfigureAwait(false);
                        return VoidResult<IError>.Success();
                    }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0424").Should().BeTrue();
    }

    /// <summary>
    ///     WriteStoryAction's exact shape, reproduced to find why the diagnostic stayed silent on it in
    ///     a real application while firing on a probe two lines away: a two-error base, a permission, an
    ///     endpoint, and the call inside a foreach.
    /// </summary>
    [Fact]
    public void TheRealCallersShape_ReportsPrag0424()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Repository;
            using Pragmatic.Result;

            namespace Other.Knowledge
            {
                public partial class KnowledgeBoundary;

                [BoundaryActions<KnowledgeBoundary>]
                public interface IKnowledgeItemsActions
                {
                    Task<Result<int, IError>> IngestText(Guid workItemId, string text, string slot = "",
                        CancellationToken ct = default);
                }

                [BoundaryActions<KnowledgeBoundary>]
                public interface IKnowledgeActions
                {
                    IKnowledgeItemsActions Items { get; }
                }
            }

            namespace TestApp.Work
            {
                [Boundary]
                public partial class WorkBoundary;

                public class WorkItem : IEntity
                {
                    public Guid PersistenceId { get; set; }
                }

                public sealed record NotFound : Error
                {
                    public override string Code => "NOT_FOUND";
                    public override int StatusCode => 404;
                }

                public readonly record struct WriteStoryResult(Guid WorkItemId);

                [DomainAction]
                [Endpoint(HttpVerb.Post, "api/work/stories")]
                public partial class WriteStoryAction : DomainAction<WriteStoryResult, NotFound>
                {
                    private IRepository<WorkItem> _items = null!;
                    private Other.Knowledge.IKnowledgeActions _knowledge = null!;

                    public required string Title { get; init; }

                    public override async Task<Result<WriteStoryResult, IError>> Execute(
                        CancellationToken ct = default)
                    {
                        foreach (var slot in new List<string> { "a" })
                        {
                            var r = await _knowledge.Items
                                .IngestText(Guid.NewGuid(), Title, slot, ct)
                                .ConfigureAwait(false);

                            if (r.IsFailure)
                                return Result<WriteStoryResult, IError>.Failure(r.Error!);
                        }

                        return Result<WriteStoryResult, IError>.Success(new WriteStoryResult(Guid.NewGuid()));
                    }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0424").Should().BeTrue(
            "the real caller writes its own rows and calls an unguarded step through a sub-boundary");
    }

    /// <summary>
    ///     An argument that this same run is about to generate must not hide the call.
    /// </summary>
    /// <remarks>
    ///     <c>entity.Id</c> comes from the entity traits the generator emits, so at transform time the
    ///     argument does not exist, overload resolution fails, and <c>GetSymbolInfo().Symbol</c> is
    ///     null. This is what made the diagnostic silent on the one action it was built for while
    ///     firing on a probe two lines away — the probe passed <c>Guid.NewGuid()</c>. The candidates of
    ///     the failed resolution still name the method, which is all the detector needs.
    /// </remarks>
    [Fact]
    public void ArgumentGeneratedByThisSameRun_DoesNotHideTheCall()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Repository;
            using Pragmatic.Result;

            namespace Other.Knowledge
            {
                public partial class KnowledgeBoundary;

                [BoundaryActions<KnowledgeBoundary>]
                public interface IKnowledgeActions
                {
                    Task<VoidResult<IError>> Ingest(Guid id, CancellationToken ct = default);
                }
            }

            namespace TestApp.Work
            {
                [Boundary]
                public partial class WorkBoundary;

                // No Id member declared: [Entity] makes the generator emit `Id => PersistenceId`,
                // which is exactly the member the call below passes.
                [Entity]
                public partial class WorkItem : IEntity
                {
                }

                [DomainAction]
                [BelongsTo<WorkBoundary>]
                public partial class WriteStoryAction : VoidDomainAction
                {
                    private IRepository<WorkItem> _items = null!;
                    private Other.Knowledge.IKnowledgeActions _knowledge = null!;

                    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    {
                        var item = new WorkItem();
                        await _knowledge.Ingest(item.Id, ct).ConfigureAwait(false);
                        return VoidResult<IError>.Success();
                    }
                }
            }
            """;

        var result = RunGeneratorWithEntities(source);

        HasDiagnostic(result, "PRAG0424").Should().BeTrue(
            "the call is there; only its argument was not resolvable yet");
    }

    /// <summary>
    ///     <c>[Transactional]</c> promises that everything here is one transaction. A call into another
    ///     boundary cannot be part of it, so the promise is refused rather than quietly narrowed.
    /// </summary>
    [Fact]
    public void TransactionalAction_CallingAnotherBoundary_IsAnError()
    {
        var result = RunGeneratorWithEntities(Source(actionAttributes: "[Transactional]"));

        HasDiagnostic(result, "PRAG0426").Should().BeTrue(
            "a rollback here cannot reach writes committed through another unit of work");
    }

    /// <summary>
    ///     And PRAG0424 stays out of it: asking for a decision about a promise already refused would
    ///     send the author to record something the error has settled.
    /// </summary>
    [Fact]
    public void TransactionalAction_CallingAnotherBoundary_DoesNotAlsoReportPrag0424()
    {
        var result = RunGeneratorWithEntities(Source(actionAttributes: "[Transactional]"));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse();
    }

    [Fact]
    public void TransactionalAction_StayingInItsOwnBoundary_IsAccepted()
    {
        var result = RunGeneratorWithEntities(
            Source(actionAttributes: "[Transactional]", foreignFacade: false));

        HasDiagnostic(result, "PRAG0426").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(" | ", GetCompilationErrors(result).Take(3).Select(e => e.ToString())));
    }

    /// <summary>The declaration reaches the invoker, or the transaction is never opened.</summary>
    [Fact]
    public void TransactionalAction_GeneratesTheHookOnItsInvoker()
    {
        var result = RunGeneratorWithEntities(
            Source(actionAttributes: "[Transactional]", foreignFacade: false));

        GetGeneratedSource(result, "WriteStoryAction.Invoker")
            .Should().Contain("IsTransactional => true");
    }

    // =========================================================================
    // PRAG0428 — composing without saying how the steps commit
    // =========================================================================

    private static string ComposingSource(string attributes) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Commit;
        using Pragmatic.Actions.Invoker;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;

        namespace TestApp.Review;

        [Boundary]
        public partial class ReviewBoundary;

        public class Term : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [DomainAction]
        [BelongsTo<ReviewBoundary>]
        public partial class ApproveOneAction : VoidDomainAction
        {
            private IRepository<Term> _terms = null!;

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        {{attributes}}
        [DomainAction]
        [BelongsTo<ReviewBoundary>]
        public partial class ApproveManyAction : VoidDomainAction
        {
            private IVoidDomainActionInvoker<ApproveOneAction> _approve = null!;

            public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            {
                await _approve.InvokeAsync(new ApproveOneAction(), ct);
                return VoidResult<IError>.Success();
            }
        }
        """;

    [Fact]
    public void ComposingWithoutDeclaringHowStepsCommit_ReportsPrag0428()
    {
        HasDiagnostic(RunGeneratorWithEntities(ComposingSource("")), "PRAG0428").Should().BeTrue(
            "three outcomes are possible and none of them is implied by the code");
    }

    [Theory]
    [InlineData("[Transactional]")]
    [InlineData("[CommitStrategy(CommitMode.Once)]")]
    [InlineData("[CommitStrategy(CommitMode.PerStep)]")]
    public void ComposingWithADeclaredStrategy_IsAccepted(string attribute)
    {
        HasDiagnostic(RunGeneratorWithEntities(ComposingSource(attribute)), "PRAG0428")
            .Should().BeFalse();
    }

    /// <summary>
    ///     An action that invokes nothing is one transaction whatever the default, so it has nothing to
    ///     declare — and asking it to would put an attribute on almost every action in a codebase.
    /// </summary>
    [Fact]
    public void AnActionThatComposesNothing_IsNotAsked()
    {
        HasDiagnostic(RunGeneratorWithEntities(Source(foreignFacade: false)), "PRAG0428")
            .Should().BeFalse();
    }

    // =========================================================================
    // PRAG0429 — the undo across a boundary, named for what it is
    // =========================================================================

    /// <summary>
    ///     The step declares an undo, so PRAG0424 stops asking — and PRAG0429 starts saying what the
    ///     answer costs.
    /// </summary>
    [Fact]
    public void ACompensatedForeignStep_IsNamedASagaWithoutALog()
    {
        var result = RunGeneratorWithEntities(Source(foreignFacade: true, foreignStepCompensable: true));

        HasDiagnostic(result, "PRAG0424").Should().BeFalse("the decision was recorded");
        HasDiagnostic(result, "PRAG0429").Should().BeTrue(
            "recording it does not make the undo durable, and the reader of the call cannot see that");
    }

    /// <summary>
    ///     Nothing crosses a boundary, so there is nothing to undo and nothing to say.
    /// </summary>
    [Fact]
    public void AnActionInsideItsOwnBoundary_IsNotToldAboutSagas()
    {
        HasDiagnostic(RunGeneratorWithEntities(Source(foreignFacade: false)), "PRAG0429")
            .Should().BeFalse();
    }

    /// <summary>
    ///     A cross-boundary call with no undo at all: PRAG0424's business, not this one's.
    /// </summary>
    [Fact]
    public void AnUnguardedForeignStep_IsNotReportedAsCompensation()
    {
        var result = RunGeneratorWithEntities(Source(foreignFacade: true));

        HasDiagnostic(result, "PRAG0424").Should().BeTrue();
        HasDiagnostic(result, "PRAG0429").Should().BeFalse("there is no undo to describe");
    }

    // =========================================================================
    // The commit strategy a boundary declares once, for all its actions
    // =========================================================================

    private static string BoundaryDefaultSource(string boundaryAttributes, string actionAttributes) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Commit;
        using Pragmatic.Actions.Invoker;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;

        namespace TestApp.Review;

        {{boundaryAttributes}}
        [Boundary]
        public partial class ReviewBoundary;

        public class Term : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [DomainAction]
        [BelongsTo<ReviewBoundary>]
        public partial class ApproveOneAction : VoidDomainAction
        {
            private IRepository<Term> _terms = null!;

            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        {{actionAttributes}}
        [DomainAction]
        [BelongsTo<ReviewBoundary>]
        public partial class ApproveManyAction : VoidDomainAction
        {
            private IVoidDomainActionInvoker<ApproveOneAction> _approve = null!;

            public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            {
                await _approve.InvokeAsync(new ApproveOneAction(), ct);
                return VoidResult<IError>.Success();
            }
        }
        """;

    /// <summary>
    ///     Declared once on the boundary, and every composing action of that boundary has answered.
    /// </summary>
    /// <remarks>
    ///     A boundary is a transaction boundary, so "how work here commits" is a sentence it can say.
    ///     Without this the diagnostic would ask the same question of every action in a codebase whose
    ///     answer never varies.
    /// </remarks>
    [Fact]
    public void ACommitStrategyOnTheBoundary_AnswersForItsActions()
    {
        var result = RunGeneratorWithEntities(
            BoundaryDefaultSource("[CommitStrategy(CommitMode.Once)]", ""));

        HasDiagnostic(result, "PRAG0428").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse();
    }

    /// <summary>Without it, the same source is asked — so the test above measures the boundary.</summary>
    [Fact]
    public void WithoutIt_TheSameActionIsAsked()
    {
        HasDiagnostic(RunGeneratorWithEntities(BoundaryDefaultSource("", "")), "PRAG0428")
            .Should().BeTrue();
    }

    /// <summary>
    ///     The nearer declaration wins: the reader of the action file sees the one that applies.
    /// </summary>
    [Fact]
    public void TheActionOverridesItsBoundary()
    {
        var sources = GetGeneratedSourcesAsDictionary(RunGeneratorWithEntities(
            BoundaryDefaultSource("[CommitStrategy(CommitMode.Once)]", "[CommitStrategy(CommitMode.PerStep)]")));

        sources.First(kv => kv.Key.Contains("ApproveManyAction.Invoker")).Value
            .Should().Contain("CommitMode.PerStep",
                "the attribute on the action is the one its own file shows");
    }

    /// <summary>
    ///     The boundary's mode reaches the generated invoker, not just the diagnostic.
    /// </summary>
    /// <remarks>
    ///     Silencing PRAG0428 without changing what is emitted would be the worst of both: a question
    ///     answered and an answer ignored.
    /// </remarks>
    [Fact]
    public void TheBoundarysMode_ReachesTheInvoker()
    {
        var sources = GetGeneratedSourcesAsDictionary(RunGeneratorWithEntities(
            BoundaryDefaultSource("[CommitStrategy(CommitMode.PerStep)]", "")));

        sources.First(kv => kv.Key.Contains("ApproveManyAction.Invoker")).Value
            .Should().Contain("CommitMode.PerStep");
    }

    /// <summary>
    ///     A boundary cannot open a transaction, and saying nothing would be another inert attribute.
    /// </summary>
    [Fact]
    public void TransactionalOnABoundary_IsReported()
    {
        HasDiagnostic(RunGeneratorWithEntities(BoundaryDefaultSource("[Transactional]", "")), "PRAG0431")
            .Should().BeTrue();
    }

    [Fact]
    public void ACommitStrategyOnABoundary_IsNotReportedAsATransaction()
    {
        HasDiagnostic(
            RunGeneratorWithEntities(BoundaryDefaultSource("[CommitStrategy(CommitMode.Once)]", "")),
            "PRAG0431").Should().BeFalse();
    }

    // =========================================================================
    // Composing through the boundary's own facade
    // =========================================================================

    /// <summary>
    ///     The facade is the surface the generator emits for composing inside a boundary, and it was
    ///     the one form PRAG0428 could not see — so the idiomatic way went unasked while the raw
    ///     invoker was caught. Backwards, and now closed.
    /// </summary>
    [Fact]
    public void ComposingThroughItsOwnFacade_IsAsked()
    {
        var result = RunGeneratorWithEntities(Source(foreignFacade: false, ownBoundaryFacade: true));

        HasDiagnostic(result, "PRAG0428").Should().BeTrue();
        HasDiagnostic(result, "PRAG0424").Should().BeFalse(
            "nothing crosses a boundary — that stays exactly as true as it was");
    }

    [Fact]
    public void ComposingThroughItsOwnFacade_WithADeclaredStrategy_IsAccepted()
    {
        HasDiagnostic(
            RunGeneratorWithEntities(Source(
                actionAttributes: "[CommitStrategy(CommitMode.Once)]",
                foreignFacade: false, ownBoundaryFacade: true)),
            "PRAG0428").Should().BeFalse();
    }

    // =========================================================================
    // A commit declaration with nothing to govern (PRAG0432)
    // =========================================================================

    /// <summary>
    ///     An assembly that declares no <c>[Boundary]</c> unless asked: that is the shape in which
    ///     an action really has none, now that one declared in the assembly is inherited.
    /// </summary>
    private static string NoBoundarySource(string attributes, bool declareBoundary = false) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Commit;
        using Pragmatic.Actions.Invoker;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Review;

        {{(declareBoundary ? "[Boundary]" : "")}}
        public partial class ReviewBoundary;

        public class Term : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        [DomainAction]
        public partial class ApproveOneAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        {{attributes}}
        [DomainAction]
        public partial class ApproveManyAction : VoidDomainAction
        {
            private IVoidDomainActionInvoker<ApproveOneAction> _approve = null!;

            public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            {
                await _approve.InvokeAsync(new ApproveOneAction(), ct);
                return VoidResult<IError>.Success();
            }
        }
        """;

    /// <summary>
    ///     An action that only composes owns no repository, so no boundary is inferred and there is no
    ///     unit of work for the attribute to act on, so the attribute would otherwise generate nothing
    ///     and say nothing.
    /// </summary>
    [Fact]
    public void TransactionalWithoutABoundary_IsReported()
    {
        HasDiagnostic(RunGeneratorWithEntities(NoBoundarySource("[Transactional]")), "PRAG0432")
            .Should().BeTrue();
    }

    [Fact]
    public void ACommitStrategyWithoutABoundary_IsReported()
    {
        HasDiagnostic(
            RunGeneratorWithEntities(NoBoundarySource("[CommitStrategy(CommitMode.Once)]")), "PRAG0432")
            .Should().BeTrue();
    }

    /// <summary>Naming the boundary is the fix, and it must silence it.</summary>
    [Fact]
    public void WithTheBoundaryNamed_ItIsAccepted()
    {
        HasDiagnostic(
            RunGeneratorWithEntities(
                NoBoundarySource("[Transactional] [BelongsTo<ReviewBoundary>]")), "PRAG0432")
            .Should().BeFalse();
    }

    /// <summary>And an action that declares nothing is not asked about a unit of work it never wanted.</summary>
    [Fact]
    public void AnActionThatDeclaresNoCommitStrategy_IsNotReported()
    {
        HasDiagnostic(RunGeneratorWithEntities(NoBoundarySource("")), "PRAG0432").Should().BeFalse();
    }

    /// <summary>
    ///     The control that draws the line: the same composing action, in an assembly that declares a
    ///     boundary, inherits it and has a unit of work — so the warning would be a false positive.
    /// </summary>
    /// <remarks>
    ///     It matters beyond tidiness: a warning that fires here prescribes <c>[BelongsTo]</c> on an
    ///     action that already inherits its boundary, teaching every author a declaration that adds
    ///     nothing.
    /// </remarks>
    [Fact]
    public void ComposingActionInAnAssemblyWithABoundary_InheritsItAndIsSilent()
    {
        HasDiagnostic(
            RunGeneratorWithEntities(NoBoundarySource("[Transactional]", declareBoundary: true)),
            "PRAG0432")
            .Should().BeFalse();
    }
}
