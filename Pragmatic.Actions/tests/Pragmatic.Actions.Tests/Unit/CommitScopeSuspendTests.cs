using Pragmatic.Actions.Commit;
using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Stepping outside the commit scope, which is what makes post-commit work its own operation.
/// </summary>
/// <remarks>
///     <para>
///         Domain events are dispatched after the commit but <em>inside</em> the <c>using</c> that holds
///         the claim. A handler that wrote through the same unit of work was therefore read as one more
///         nested step: <c>CommitsHere</c> false, the covering batch still deferring, so its writes were
///         staged and never saved — and the handler returned success.
///     </para>
///     <para>
///         Measured, not deduced: declaring a glossary term in a consumer application ran the handler
///         (the log said so), the sweep it invokes matched the right rows, and the candidate it settled
///         was still pending afterwards. Nothing threw.
///     </para>
/// </remarks>
public class CommitScopeSuspendTests
{
    /// <summary>A unit of work that does nothing — the scope keys on identity, never on behaviour.</summary>
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException("The scope keys on identity; nothing here transacts.");

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void WithinAClaim_ANestedInvocationDoesNotOwnTheCommit()
    {
        // The behaviour being suspended, asserted first so the test below is not comparing against a
        // scope that never claimed anything.
        var uow = new FakeUnitOfWork();
        using var outer = CommitScope.Claim(uow, CommitMode.Once);

        outer.CommitsHere.Should().BeTrue();

        using var nested = CommitScope.Claim(uow, CommitMode.Once);
        nested.CommitsHere.Should().BeFalse("an outer claim on the same unit of work owns the commit");
    }

    [Fact]
    public void Suspended_AnInvocationOwnsItsOwnCommitAgain()
    {
        var uow = new FakeUnitOfWork();
        using var outer = CommitScope.Claim(uow, CommitMode.Once);

        using (CommitScope.Suspend())
        {
            using var afterTheCommit = CommitScope.Claim(uow, CommitMode.Once);

            afterTheCommit.CommitsHere.Should().BeTrue(
                "the commit has happened, so what runs now is its own unit of work");
        }
    }

    [Fact]
    public void Suspended_NoBatchCoversTheUnitOfWork()
    {
        // Suspending the claims alone would not be enough: a nested write asks CoveringBatch, which
        // reads the ambient BatchContext rather than the claim stack, and would still defer into a
        // batch nobody is going to flush again.
        var uow = new FakeUnitOfWork();
        using var outer = CommitScope.Claim(uow, CommitMode.Once);

        CommitScope.CoveringBatch(uow).Should().NotBeNull();

        using (CommitScope.Suspend())
        {
            CommitScope.CoveringBatch(uow).Should().BeNull();
            BatchContext.Current.Should().BeNull();
        }
    }

    [Fact]
    public void AfterTheSuspension_TheClaimAndTheBatchAreBackAsTheyWere()
    {
        // The owner still has to flush and dispose what it opened. A suspension that did not restore
        // would strand the batch and lose whatever the nested steps deferred into it.
        var uow = new FakeUnitOfWork();
        using var outer = CommitScope.Claim(uow, CommitMode.Once);
        var batchBefore = CommitScope.BatchFor(uow);

        using (CommitScope.Suspend())
        {
        }

        CommitScope.BatchFor(uow).Should().BeSameAs(batchBefore);
        BatchContext.Current.Should().BeSameAs(batchBefore);

        using var nestedAgain = CommitScope.Claim(uow, CommitMode.Once);
        nestedAgain.CommitsHere.Should().BeFalse("the outer claim is back in force");
    }
}
