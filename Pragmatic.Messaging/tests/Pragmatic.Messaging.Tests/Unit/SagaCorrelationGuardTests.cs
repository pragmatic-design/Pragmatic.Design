using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Saga;
using Pragmatic.Messaging.Tests.EFCore;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>A correlation id over the persisted column limit fails cleanly, not as a raw DB error.</summary>
public class SagaCorrelationGuardTests
{
    [Fact]
    public void EnsureValidCorrelationId_OverLimit_ThrowsWithGuidance()
    {
        var act = () => SagaInstance.EnsureValidCorrelationId(new string('c', SagaInstance.MaxCorrelationIdLength + 1));
        act.Should().Throw<ArgumentException>().WithMessage("*maximum is 128*");
    }

    [Fact]
    public void EnsureValidCorrelationId_AtLimit_DoesNotThrow()
    {
        var act = () => SagaInstance.EnsureValidCorrelationId(new string('c', SagaInstance.MaxCorrelationIdLength));
        act.Should().NotThrow();
    }

    [Fact]
    public async Task InMemorySaveAsync_TooLongCorrelation_ThrowsCleanly()
    {
        var repo = new InMemorySagaRepository<OrderSaga>(s => s.Id, s => s.CorrelationId, s => s.CompletedAt);
        var saga = new OrderSaga { Id = Guid.NewGuid(), CorrelationId = new string('c', 200) };

        Func<Task> act = () => repo.SaveAsync(saga);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
