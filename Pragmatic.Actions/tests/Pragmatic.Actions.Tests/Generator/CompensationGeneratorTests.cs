using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     <c>[UndoWith&lt;T&gt;]</c>: the undo of a step that has already committed.
///     <para>
///         PRAG0424 says two boundaries commit and asks for a decision. This is the decision that
///         actually repairs something instead of accepting the damage — best effort and in-request,
///         which is the whole guarantee and is stated as such on the attribute.
///     </para>
/// </summary>
public class CompensationGeneratorTests : ActionsGeneratorTestBase
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Actions.Compensation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Repository;
        using Pragmatic.Result;

        namespace TestApp.Knowledge;

        [Boundary]
        public partial class KnowledgeBoundary;

        public class Term : IEntity
        {
            public Guid PersistenceId { get; set; }
        }

        public sealed record IngestResult(int TermsAdded);
        """;

    private static string Source(string compensator, string attribute) => $$"""
        {{Preamble}}

        {{compensator}}

        {{attribute}}
        [DomainAction]
        [BelongsTo<KnowledgeBoundary>]
        public partial class IngestTextAction : DomainAction<IngestResult>
        {
            private IRepository<Term> _terms = null!;

            public override Task<Result<IngestResult, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<IngestResult, IError>.Success(new IngestResult(1)));
        }
        """;

    private const string MatchingCompensator = """
        public sealed class RemoveIngestedText : ICompensates<IngestResult>
        {
            public Task<VoidResult<IError>> Undo(IngestResult committed, CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    private const string WrongCompensator = """
        public sealed class RemoveSomethingElse : ICompensates<string>
        {
            public Task<VoidResult<IError>> Undo(string committed, CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    [Fact]
    public void UndoWith_GeneratesTheUndoOnTheInvoker()
    {
        var result = RunGeneratorWithEntities(
            Source(MatchingCompensator, "[UndoWith<RemoveIngestedText>]"));

        HasCompilationErrors(result).Should().BeFalse();

        var invoker = GetGeneratedSource(result, "IngestTextAction.Invoker");
        invoker.Should().NotBeNull();
        invoker.Should().Contain("IsCompensable => true");
        invoker.Should().Contain("compensator.Undo(committed, ct)");
    }

    /// <summary>
    ///     The commit is the point. A compensator that stages its writes and never saves them is a
    ///     no-op that reports success — the exact failure mode the attribute exists to close.
    /// </summary>
    [Fact]
    public void GeneratedUndo_CommitsThroughTheBoundarysUnitOfWork()
    {
        var result = RunGeneratorWithEntities(
            Source(MatchingCompensator, "[UndoWith<RemoveIngestedText>]"));

        var invoker = GetGeneratedSource(result, "IngestTextAction.Invoker")!;

        var undoIndex = invoker.IndexOf("compensator.Undo", StringComparison.Ordinal);
        var saveIndex = invoker.IndexOf("SaveChangesAsync(ct)", undoIndex, StringComparison.Ordinal);

        saveIndex.Should().BeGreaterThan(undoIndex, "the undo has to be committed after it is staged");
    }

    [Fact]
    public void UndoWith_RegistersTheCompensatorAndTheScope()
    {
        var result = RunGeneratorWithEntities(
            Source(MatchingCompensator, "[UndoWith<RemoveIngestedText>]"));

        var registration = GetGeneratedSource(result, "Actions.Registration")!;

        registration.Should().Contain("ICompensationScope");
        registration.Should().Contain("RemoveIngestedText");
    }

    /// <summary>
    ///     An application that declares no compensator must register no scope: the mechanism costs it
    ///     nothing, and every invoker's compensation branch resolves null and does nothing.
    /// </summary>
    [Fact]
    public void NoCompensator_RegistersNoScope()
    {
        var result = RunGeneratorWithEntities(Source(MatchingCompensator, ""));

        GetGeneratedSource(result, "Actions.Registration")
            .Should().NotContain("ICompensationScope");
    }

    [Fact]
    public void CompensatorForAnotherReturnType_ReportsPrag0425()
    {
        var result = RunGeneratorWithEntities(
            Source(WrongCompensator, "[UndoWith<RemoveSomethingElse>]"));

        HasDiagnostic(result, "PRAG0425").Should().BeTrue(
            "a mismatched compensator generates nothing while looking like a decision");
    }

    [Fact]
    public void MatchingCompensator_ReportsNoPrag0425()
    {
        var result = RunGeneratorWithEntities(
            Source(MatchingCompensator, "[UndoWith<RemoveIngestedText>]"));

        HasDiagnostic(result, "PRAG0425").Should().BeFalse();
    }

    /// <summary>
    ///     The facade tells a caller in another boundary that <i>this step</i> undoes itself. Per method,
    ///     not per facade: a caller that invokes one compensable operation should not be asked to
    ///     compensate the ones it never touches.
    /// </summary>
    [Fact]
    public void CompensableAction_MarksItsFacadeMethod()
    {
        var result = RunGeneratorWithEntities(
            Source(MatchingCompensator, "[UndoWith<RemoveIngestedText>]"));

        GetGeneratedSource(result, "Definition")
            .Should().Contain("[global::Pragmatic.Actions.Attributes.CompensableStep]");
    }

    [Fact]
    public void ActionWithoutCompensator_LeavesItsFacadeMethodUnmarked()
    {
        var result = RunGeneratorWithEntities(Source(MatchingCompensator, ""));

        GetGeneratedSource(result, "Definition")
            .Should().NotContain("CompensableStep");
    }
}
