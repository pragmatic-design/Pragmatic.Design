using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Tests.Unit;

public class InMemorySagaRepositoryTests
{
    public enum TestSagaState { Created, Processing, Completed }

    public class TestSaga : ISaga<TestSagaState>
    {
        public Guid Id { get; set; }
        public TestSagaState State { get; set; }
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private static InMemorySagaRepository<TestSaga> CreateRepository()
        => new(
            idAccessor: s => s.Id,
            correlationAccessor: s => s.CorrelationId,
            completedAccessor: s => s.CompletedAt);

    private static TestSaga CreateSaga(
        Guid? id = null,
        string? correlationId = null,
        TestSagaState state = TestSagaState.Created,
        DateTimeOffset? completedAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            State = state,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = completedAt,
        };

    [Fact]
    public async Task SaveAsync_CreatesNewSaga()
    {
        var repo = CreateRepository();
        var saga = CreateSaga();

        await repo.SaveAsync(saga);

        repo.Count.Should().Be(1);
        var found = await repo.FindByIdAsync(saga.Id);
        found.Should().NotBeNull();
        found.Should().BeSameAs(saga);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingSaga()
    {
        var repo = CreateRepository();
        var saga = CreateSaga();

        await repo.SaveAsync(saga);
        saga.State = TestSagaState.Processing;
        await repo.SaveAsync(saga);

        repo.Count.Should().Be(1);
        var found = await repo.FindByIdAsync(saga.Id);
        found!.State.Should().Be(TestSagaState.Processing);
    }

    [Fact]
    public async Task FindByCorrelationAsync_ReturnsSaga_WhenExists()
    {
        var repo = CreateRepository();
        var correlationId = "order-123";
        var saga = CreateSaga(correlationId: correlationId);

        await repo.SaveAsync(saga);

        var found = await repo.FindByCorrelationAsync(correlationId);
        found.Should().NotBeNull();
        found!.Id.Should().Be(saga.Id);
        found.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task FindByCorrelationAsync_ReturnsNull_WhenNotFound()
    {
        var repo = CreateRepository();
        var saga = CreateSaga(correlationId: "order-123");
        await repo.SaveAsync(saga);

        var found = await repo.FindByCorrelationAsync("order-999");
        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsSaga_WhenExists()
    {
        var repo = CreateRepository();
        var saga = CreateSaga();
        await repo.SaveAsync(saga);

        var found = await repo.FindByIdAsync(saga.Id);
        found.Should().NotBeNull();
        found.Should().BeSameAs(saga);
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNull_WhenNotFound()
    {
        var repo = CreateRepository();
        var saga = CreateSaga();
        await repo.SaveAsync(saga);

        var found = await repo.FindByIdAsync(Guid.NewGuid());
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyNonCompleted()
    {
        var repo = CreateRepository();

        var activeSaga1 = CreateSaga(state: TestSagaState.Created);
        var activeSaga2 = CreateSaga(state: TestSagaState.Processing);
        var completedSaga = CreateSaga(
            state: TestSagaState.Completed,
            completedAt: DateTimeOffset.UtcNow);

        await repo.SaveAsync(activeSaga1);
        await repo.SaveAsync(activeSaga2);
        await repo.SaveAsync(completedSaga);

        var active = await repo.GetActiveAsync();
        active.Should().HaveCount(2);
        active.Should().Contain(activeSaga1);
        active.Should().Contain(activeSaga2);
        active.Should().NotContain(completedSaga);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsEmpty_WhenAllCompleted()
    {
        var repo = CreateRepository();

        var completed1 = CreateSaga(
            state: TestSagaState.Completed,
            completedAt: DateTimeOffset.UtcNow);
        var completed2 = CreateSaga(
            state: TestSagaState.Completed,
            completedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await repo.SaveAsync(completed1);
        await repo.SaveAsync(completed2);

        var active = await repo.GetActiveAsync();
        active.Should().BeEmpty();
    }
}
