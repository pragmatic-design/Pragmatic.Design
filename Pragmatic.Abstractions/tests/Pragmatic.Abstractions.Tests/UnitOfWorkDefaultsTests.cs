using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class UnitOfWorkDefaultsTests
{
    // Minimal stub overriding ONLY the abstract members so the default interface methods
    // (State, IsCommitted, Add, SavepointAsync, RollbackToSavepointAsync) are exercised.
    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotImplementedException();

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void State_DefaultsToNone()
    {
        IUnitOfWork uow = new StubUnitOfWork();

        uow.State.Should().Be(TransactionState.None);
    }

    [Fact]
    public void IsCommitted_DefaultsToFalse()
    {
        IUnitOfWork uow = new StubUnitOfWork();

        uow.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public void Add_DefaultThrowsNotSupported()
    {
        IUnitOfWork uow = new StubUnitOfWork();

        var act = () => uow.Add(new object());

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task SavepointAsync_DefaultThrowsNotSupported()
    {
        IUnitOfWork uow = new StubUnitOfWork();

        var act = () => uow.SavepointAsync("sp1");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task RollbackToSavepointAsync_DefaultThrowsNotSupported()
    {
        IUnitOfWork uow = new StubUnitOfWork();

        var act = () => uow.RollbackToSavepointAsync("sp1");

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
