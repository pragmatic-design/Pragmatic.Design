#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Saga;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.EFCore;

/// <summary>
///     Two messages for one saga, handled at once: each loads the state, changes it, and saves. The one
///     that saves second read a version the first has since replaced, and must lose — otherwise its save
///     overwrites the first one's change, and the saga forgets a message it already acknowledged.
/// </summary>
/// <remarks>
///     ⚠️ The version compared must be the one <b>read with the state</b>, not one read again at save time:
///     a load is <c>AsNoTracking</c>, and a save that re-queries the row sees the other writer's version
///     and wins against it. Seen as Warehouse's fulfilment process stuck in <c>Compensating</c> with one of
///     its two acknowledgements lost.
/// </remarks>
public sealed class ASagaSavedAfterAnotherWriteSinceItsReadTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private readonly List<TestSagaDbContext> _contexts = [];

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        await using var setup = NewContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task TheSecondSave_OfAStateReadBeforeTheFirst_IsAConflict()
    {
        var sagaId = await StartedSagaAsync();
        var first = NewRepository();
        var second = NewRepository();
        var readByFirst = (await first.FindByIdAsync(sagaId))!;
        var readBySecond = (await second.FindByIdAsync(sagaId))!;

        readByFirst.OrderNumber = "changed by the first";
        await first.SaveWithStepAsync(readByFirst, timeoutAt: null, "First");
        readBySecond.TotalAmount = 99m;
        var save = () => second.SaveWithStepAsync(readBySecond, timeoutAt: null, "Second");

        // The saga's own conflict, not EF's: the generated orchestrator reads it again on this type.
        (await save.Should().ThrowAsync<SagaConcurrencyException>()).Which.InnerException
            .Should().BeOfType<DbUpdateConcurrencyException>();
        var stored = (await NewRepository().FindByIdAsync(sagaId))!;
        stored.OrderNumber.Should().Be("changed by the first", "the first save is not overwritten");
    }

    /// <summary>
    ///     The save that lost leaves nothing behind in its context: read again and saved again, as
    ///     the orchestrator now does, the step is recorded once.
    /// </summary>
    /// <remarks>
    ///     The context is the message's own, and the retry runs in it. Left tracked, the lost save's step row
    ///     — and its outbox messages — would be inserted again together with the retry's.
    /// </remarks>
    [Fact]
    public async Task ASaveThatLost_ReadAgainAndSavedAgain_RecordsItsStepOnce()
    {
        var sagaId = await StartedSagaAsync();
        var first = NewRepository();
        var second = NewRepository();
        var readByFirst = (await first.FindByIdAsync(sagaId))!;
        var readBySecond = (await second.FindByIdAsync(sagaId))!;
        readByFirst.OrderNumber = "changed by the first";
        await first.SaveWithStepAsync(readByFirst, timeoutAt: null, "First");
        readBySecond.TotalAmount = 99m;
        var lost = () => second.SaveWithStepAsync(readBySecond, timeoutAt: null, "Second");
        await lost.Should().ThrowAsync<SagaConcurrencyException>();

        var readAgain = (await second.FindByIdAsync(sagaId))!;
        readAgain.TotalAmount = 99m;
        await second.SaveWithStepAsync(readAgain, timeoutAt: null, "Second");

        var stored = (await NewRepository().FindByIdAsync(sagaId))!;
        (stored.OrderNumber, stored.TotalAmount).Should().Be(("changed by the first", 99m));
        (await NewRepository().GetExecutedStepNamesAsync(sagaId)).Where(step => step == "Second")
            .Should().ContainSingle("the lost save's step row went with it");
    }

    /// <summary>
    ///     The control: saves one after another, each from a fresh read, both succeed — the version moves
    ///     with the state, so a writer that read the latest one is not refused.
    /// </summary>
    [Fact]
    public async Task SavesInTurn_EachFromAFreshRead_BothSucceed()
    {
        var sagaId = await StartedSagaAsync();

        var first = NewRepository();
        var readByFirst = (await first.FindByIdAsync(sagaId))!;
        readByFirst.OrderNumber = "changed by the first";
        await first.SaveWithStepAsync(readByFirst, timeoutAt: null, "First");

        var second = NewRepository();
        var readBySecond = (await second.FindByIdAsync(sagaId))!;
        readBySecond.TotalAmount = 99m;
        await second.SaveWithStepAsync(readBySecond, timeoutAt: null, "Second");

        var stored = (await NewRepository().FindByIdAsync(sagaId))!;
        (stored.OrderNumber, stored.TotalAmount).Should().Be(("changed by the first", 99m));
    }

    /// <summary>
    ///     The control for a repository that saves twice without reading in between — what the orchestrator
    ///     does not do, but a caller may: its own save is not a conflict with itself.
    /// </summary>
    [Fact]
    public async Task OneRepository_SavingTwiceAfterOneRead_IsNotAConflictWithItself()
    {
        var sagaId = await StartedSagaAsync();
        var repository = NewRepository();
        var saga = (await repository.FindByIdAsync(sagaId))!;

        saga.OrderNumber = "once";
        await repository.SaveAsync(saga);
        saga.OrderNumber = "twice";
        await repository.SaveAsync(saga);

        (await NewRepository().FindByIdAsync(sagaId))!.OrderNumber.Should().Be("twice");
    }

    private async Task<Guid> StartedSagaAsync()
    {
        var saga = new OrderSaga
        {
            Id = Guid.NewGuid(),
            CorrelationId = $"order-{Guid.NewGuid():N}",
            State = OrderSagaState.PaymentPending,
            StartedAt = DateTimeOffset.UtcNow,
            OrderNumber = "ORD-1",
            TotalAmount = 10m,
        };
        await NewRepository().SaveAsync(saga);
        return saga.Id;
    }

    private EfCoreSagaRepository<OrderSaga, OrderSagaState> NewRepository()
        => new(NewContext(), NullLogger<EfCoreSagaRepository<OrderSaga, OrderSagaState>>.Instance);

    private TestSagaDbContext NewContext()
    {
        var context = new TestSagaDbContext(new DbContextOptionsBuilder<TestSagaDbContext>().UseSqlite(_connection).Options);
        _contexts.Add(context);
        return context;
    }
}
