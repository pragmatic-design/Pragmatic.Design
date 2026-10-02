using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The savepoint members are default-throwing (same contract as <c>ITenantStore.DeleteAsync</c>):
///     a unit of work that cannot honor savepoints must fail loudly, never silently no-op.
/// </summary>
public sealed class UnitOfWorkSavepointDefaultsTests
{
    private sealed class MinimalUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Savepoint_DefaultImplementation_ThrowsNotSupported()
    {
        IUnitOfWork uow = new MinimalUnitOfWork();

        var act = () => uow.SavepointAsync("sp1");
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task RollbackToSavepoint_DefaultImplementation_ThrowsNotSupported()
    {
        IUnitOfWork uow = new MinimalUnitOfWork();

        var act = () => uow.RollbackToSavepointAsync("sp1");
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
