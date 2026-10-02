using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     <c>[Transactional]</c> as the pipeline runs it: a transaction opened before the body, committed
///     on success, rolled back on failure and on an exception.
/// </summary>
/// <remarks>
///     <para>
///         Measured against a unit of work that records what it was asked to do, because the questions
///         here are about the sequence of calls rather than about rows. Whether the rollback reaches
///         the database is PostgreSQL's job, not this pipeline's.
///     </para>
///     <para>
///         The end-to-end half is stated as missing rather than faked: the Showcase has no action whose
///         steps read each other's writes, which is the only case that justifies the attribute, so the
///         behaviour has no honest E2E to live in yet.
///     </para>
/// </remarks>
public class TransactionalPipelineTests
{
    private sealed class TestError(string code) : IError
    {
        public string Code { get; } = code;
        public int StatusCode => 500;
        public string Title => Code;
    }

    /// <summary>Records the calls; nothing here touches a database.</summary>
    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public List<string> Calls { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Calls.Add("save");
            return Task.FromResult(0);
        }

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
        {
            Calls.Add("begin");
            return Task.FromResult<ITransaction>(new RecordingTransaction(Calls));
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingTransaction(List<string> calls) : ITransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public Task CommitAsync(CancellationToken ct = default)
        {
            calls.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            calls.Add("rollback");
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class WorkAction(bool succeeds, bool throws = false) : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        {
            if (throws)
                throw new InvalidOperationException("boom");

            return Task.FromResult(succeeds
                ? VoidResult<IError>.Success()
                : VoidResult<IError>.Failure(new TestError("FAILED")));
        }
    }

    /// <summary>Stands in for the generated invoker of an action marked <c>[Transactional]</c>.</summary>
    private sealed class WorkInvoker(IServiceProvider serviceProvider, RecordingUnitOfWork unitOfWork)
        : VoidDomainActionInvoker<WorkAction>(serviceProvider)
    {
        protected override void InjectDependencies(WorkAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override bool IsTransactional => true;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }

    private static (WorkInvoker Invoker, RecordingUnitOfWork UnitOfWork) Build()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var services = new ServiceCollection();
        services.AddLogging();

        return (new WorkInvoker(services.BuildServiceProvider(), unitOfWork), unitOfWork);
    }

    [Fact]
    public async Task ATransactionalAction_OpensBeforeTheBodyAndCommitsAfterTheSave()
    {
        var (invoker, unitOfWork) = Build();

        (await invoker.InvokeAsync(new WorkAction(succeeds: true))).IsSuccess.Should().BeTrue();

        unitOfWork.Calls.Should().Equal(["begin", "save", "commit"],
            "the order is the guarantee: opened before anything runs so every nested save lands "
            + "inside it, committed after the action's own");
    }

    [Fact]
    public async Task ATransactionalAction_ThatFails_RollsBackAndDoesNotCommit()
    {
        var (invoker, unitOfWork) = Build();

        (await invoker.InvokeAsync(new WorkAction(succeeds: false))).IsFailure.Should().BeTrue();

        unitOfWork.Calls.Should().Equal(["begin", "rollback"],
            "no save, and the transaction is undone rather than left open");
    }

    /// <summary>
    ///     The case that leaks a connection when it is forgotten: an exception, not a failed result.
    /// </summary>
    [Fact]
    public async Task ATransactionalAction_ThatThrows_RollsBackBeforeRethrowing()
    {
        var (invoker, unitOfWork) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeAsync(new WorkAction(succeeds: false, throws: true)));

        unitOfWork.Calls.Should().Equal(["begin", "rollback"]);
    }
}
