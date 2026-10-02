#pragma warning disable CA2007 // xUnit manages SynchronizationContext

using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Tests.EFCore;

// ─────────────────────────────────────────────────────────────────────────────
// Test saga types
// ─────────────────────────────────────────────────────────────────────────────

public enum OrderSagaState
{
    Created = 0,
    PaymentPending = 1,
    PaymentConfirmed = 2,
    Shipped = 3,
    Completed = 4
}

public class OrderSaga : ISaga<OrderSagaState>
{
    public Guid Id { get; set; }
    public OrderSagaState State { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Saga-specific data persisted in StateData JSON.</summary>
    public string? OrderNumber { get; set; }
    public decimal TotalAmount { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test DbContext for saga tables
// ─────────────────────────────────────────────────────────────────────────────

public class TestSagaDbContext(DbContextOptions<TestSagaDbContext> options) : DbContext(options)
{
    public DbSet<SagaInstance> SagaInstances => Set<SagaInstance>();
    public DbSet<SagaStep> SagaSteps => Set<SagaStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var config = new SagaEntityTypeConfiguration();
        config.Configure(modelBuilder.Entity<SagaInstance>());
        config.Configure(modelBuilder.Entity<SagaStep>());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SagaInstance / SagaStep entity CRUD tests
// ─────────────────────────────────────────────────────────────────────────────

public class SagaEntityCrudTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestSagaDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestSagaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TestSagaDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CreateSagaInstance_PersistsCorrectly()
    {
        var sagaId = Guid.NewGuid();
        var instance = new SagaInstance
        {
            Id = sagaId,
            SagaType = "Pragmatic.Messaging.Tests.EFCore.OrderSaga",
            CorrelationId = "order-123",
            State = (int)OrderSagaState.PaymentPending,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            StateData = """{"orderNumber":"ORD-001","totalAmount":99.99}"""
        };

        _dbContext.SagaInstances.Add(instance);
        await _dbContext.SaveChangesAsync();

        var persisted = await _dbContext.SagaInstances.FindAsync(sagaId);

        persisted.Should().NotBeNull();
        persisted!.SagaType.Should().Be("Pragmatic.Messaging.Tests.EFCore.OrderSaga");
        persisted.CorrelationId.Should().Be("order-123");
        persisted.State.Should().Be((int)OrderSagaState.PaymentPending);
        persisted.Status.Should().Be(SagaStatus.Active);
        persisted.StateData.Should().Contain("ORD-001");
        persisted.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task AddSagaStep_LinkedToInstance()
    {
        var sagaId = Guid.NewGuid();
        var instance = new SagaInstance
        {
            Id = sagaId,
            SagaType = "OrderSaga",
            CorrelationId = "order-456",
            State = 0,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        };

        var step = new SagaStep
        {
            Id = Guid.NewGuid(),
            SagaInstanceId = sagaId,
            StepName = "ProcessPayment",
            Status = SagaStepStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(2),
            Input = """{"amount":50.00}""",
            Output = """{"transactionId":"tx-789"}"""
        };

        _dbContext.SagaInstances.Add(instance);
        _dbContext.SagaSteps.Add(step);
        await _dbContext.SaveChangesAsync();

        var loadedInstance = await _dbContext.SagaInstances
            .Include(s => s.Steps)
            .FirstAsync(s => s.Id == sagaId);

        loadedInstance.Steps.Should().HaveCount(1);
        loadedInstance.Steps[0].StepName.Should().Be("ProcessPayment");
        loadedInstance.Steps[0].Status.Should().Be(SagaStepStatus.Completed);
        loadedInstance.Steps[0].SagaInstanceId.Should().Be(sagaId);
    }

    [Fact]
    public async Task QueryByCorrelationId_ReturnsInstance()
    {
        _dbContext.SagaInstances.Add(new SagaInstance
        {
            Id = Guid.NewGuid(),
            SagaType = "OrderSaga",
            CorrelationId = "order-AAA",
            State = 0,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        });
        _dbContext.SagaInstances.Add(new SagaInstance
        {
            Id = Guid.NewGuid(),
            SagaType = "OrderSaga",
            CorrelationId = "order-BBB",
            State = 1,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var result = await _dbContext.SagaInstances
            .FirstOrDefaultAsync(s => s.CorrelationId == "order-AAA");

        result.Should().NotBeNull();
        result!.CorrelationId.Should().Be("order-AAA");
    }

    [Fact]
    public async Task QueryActiveInstances_ExcludesCompleted()
    {
        _dbContext.SagaInstances.Add(new SagaInstance
        {
            Id = Guid.NewGuid(),
            SagaType = "OrderSaga",
            CorrelationId = "active-1",
            State = 1,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        });
        _dbContext.SagaInstances.Add(new SagaInstance
        {
            Id = Guid.NewGuid(),
            SagaType = "OrderSaga",
            CorrelationId = "completed-1",
            State = 4,
            Status = SagaStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        _dbContext.SagaInstances.Add(new SagaInstance
        {
            Id = Guid.NewGuid(),
            SagaType = "OrderSaga",
            CorrelationId = "compensated-1",
            State = 2,
            Status = SagaStatus.Compensated,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var active = await _dbContext.SagaInstances
            .Where(s => s.Status == SagaStatus.Active)
            .ToListAsync();

        active.Should().HaveCount(1);
        active[0].CorrelationId.Should().Be("active-1");
    }

    [Fact]
    public async Task MultipleSteps_CascadeDelete()
    {
        var sagaId = Guid.NewGuid();
        var instance = new SagaInstance
        {
            Id = sagaId,
            SagaType = "OrderSaga",
            CorrelationId = "cascade-test",
            State = 0,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        };

        _dbContext.SagaInstances.Add(instance);
        _dbContext.SagaSteps.AddRange(
            new SagaStep
            {
                Id = Guid.NewGuid(),
                SagaInstanceId = sagaId,
                StepName = "Step1",
                Status = SagaStepStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow
            },
            new SagaStep
            {
                Id = Guid.NewGuid(),
                SagaInstanceId = sagaId,
                StepName = "Step2",
                Status = SagaStepStatus.Failed,
                StartedAt = DateTimeOffset.UtcNow,
                Error = "Something went wrong"
            });
        await _dbContext.SaveChangesAsync();

        // Delete saga instance — steps should cascade
        _dbContext.SagaInstances.Remove(instance);
        await _dbContext.SaveChangesAsync();

        var remainingSteps = await _dbContext.SagaSteps.CountAsync();
        remainingSteps.Should().Be(0);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EfCoreSagaRepository tests
// ─────────────────────────────────────────────────────────────────────────────

public class EfCoreSagaRepositoryTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestSagaDbContext _dbContext = null!;
    private EfCoreSagaRepository<OrderSaga, OrderSagaState> _repository = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestSagaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TestSagaDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _repository = new EfCoreSagaRepository<OrderSaga, OrderSagaState>(
            _dbContext,
            NullLogger<EfCoreSagaRepository<OrderSaga, OrderSagaState>>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static OrderSaga CreateSaga(
        string correlationId = "order-100",
        OrderSagaState state = OrderSagaState.Created,
        DateTimeOffset? completedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            State = state,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = completedAt,
            OrderNumber = "ORD-100",
            TotalAmount = 250.00m
        };

    [Fact]
    public async Task SaveAsync_NewSaga_PersistsInstance()
    {
        var saga = CreateSaga();

        await _repository.SaveAsync(saga);

        var instance = await _dbContext.SagaInstances.SingleAsync();
        instance.Id.Should().Be(saga.Id);
        instance.CorrelationId.Should().Be("order-100");
        instance.Status.Should().Be(SagaStatus.Active);
    }

    [Fact]
    public async Task SaveWithStepAsync_RecordsExecutedStepHistoryAtomically()
    {
        // Each executed step is recorded in __SagaSteps in the SAME commit as the state, so the
        // history (which path-based compensation reads) can never diverge from the saga state.
        var saga = CreateSaga(state: OrderSagaState.PaymentPending);

        await _repository.SaveWithStepAsync(saga, timeoutAt: null, "HandleOrderCreated");
        saga.State = OrderSagaState.PaymentConfirmed;
        await _repository.SaveWithStepAsync(saga, timeoutAt: null, "HandlePaymentConfirmed");

        var steps = await _dbContext.SagaSteps.AsNoTracking()
            .Where(s => s.SagaInstanceId == saga.Id).ToListAsync();
        steps.Should().HaveCount(2);
        steps.Should().OnlyContain(s => s.Status == SagaStepStatus.Completed);

        var executed = await _repository.GetExecutedStepNamesAsync(saga.Id);
        executed.Should().BeEquivalentTo("HandleOrderCreated", "HandlePaymentConfirmed");
    }

    [Fact]
    public async Task GetExecutedStepNamesAsync_NoHistory_ReturnsEmpty()
    {
        var executed = await _repository.GetExecutedStepNamesAsync(Guid.NewGuid());
        executed.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveWithTimeoutAsync_PersistsStateAndDeadlineInOneCommit()
    {
        // State and the next step deadline are written by a single save, so a crash cannot leave a saga
        // with an advanced state but a missing deadline, as a two-write save+SetTimeout path could.
        var saga = CreateSaga(state: OrderSagaState.PaymentPending);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);

        await _repository.SaveWithTimeoutAsync(saga, deadline);

        var instance = await _dbContext.SagaInstances.SingleAsync();
        instance.State.Should().Be((int)OrderSagaState.PaymentPending);
        instance.TimeoutAt.Should().NotBeNull();
        instance.TimeoutAt!.Value.Should().BeCloseTo(deadline, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SaveAsync_ExistingSaga_UpdatesState()
    {
        var saga = CreateSaga();
        await _repository.SaveAsync(saga);

        saga.State = OrderSagaState.PaymentConfirmed;
        await _repository.SaveAsync(saga);

        var instance = await _dbContext.SagaInstances.SingleAsync();
        instance.State.Should().Be((int)OrderSagaState.PaymentConfirmed);
    }

    [Fact]
    public async Task SaveAsync_CompletedSaga_SetsStatusCompleted()
    {
        var saga = CreateSaga();
        await _repository.SaveAsync(saga);

        saga.State = OrderSagaState.Completed;
        saga.CompletedAt = DateTimeOffset.UtcNow;
        await _repository.SaveAsync(saga);

        var instance = await _dbContext.SagaInstances.SingleAsync();
        instance.Status.Should().Be(SagaStatus.Completed);
        instance.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FindByCorrelationAsync_ExistingSaga_ReturnsSaga()
    {
        var saga = CreateSaga(correlationId: "corr-42");
        await _repository.SaveAsync(saga);

        var found = await _repository.FindByCorrelationAsync("corr-42");

        found.Should().NotBeNull();
        found!.Id.Should().Be(saga.Id);
        found.CorrelationId.Should().Be("corr-42");
        found.State.Should().Be(OrderSagaState.Created);
    }

    [Fact]
    public async Task FindByCorrelationAsync_NonExistent_ReturnsNull()
    {
        var found = await _repository.FindByCorrelationAsync("does-not-exist");

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdAsync_ExistingSaga_ReturnsSaga()
    {
        var saga = CreateSaga();
        await _repository.SaveAsync(saga);

        var found = await _repository.FindByIdAsync(saga.Id);

        found.Should().NotBeNull();
        found!.CorrelationId.Should().Be(saga.CorrelationId);
    }

    [Fact]
    public async Task FindByIdAsync_NonExistent_ReturnsNull()
    {
        var found = await _repository.FindByIdAsync(Guid.NewGuid());

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByCorrelationAsync_PreservesSagaSpecificData()
    {
        var saga = CreateSaga();
        saga.OrderNumber = "ORD-SPECIAL";
        saga.TotalAmount = 999.99m;
        await _repository.SaveAsync(saga);

        var found = await _repository.FindByCorrelationAsync(saga.CorrelationId);

        found.Should().NotBeNull();
        found!.OrderNumber.Should().Be("ORD-SPECIAL");
        found.TotalAmount.Should().Be(999.99m);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyActiveSagas()
    {
        var active1 = CreateSaga(correlationId: "active-A");
        var active2 = CreateSaga(correlationId: "active-B");
        var completed = CreateSaga(
            correlationId: "completed-C",
            state: OrderSagaState.Completed,
            completedAt: DateTimeOffset.UtcNow);

        await _repository.SaveAsync(active1);
        await _repository.SaveAsync(active2);
        await _repository.SaveAsync(completed);

        var activeList = await _repository.GetActiveAsync();

        activeList.Should().HaveCount(2);
        activeList.Should().OnlyContain(s => s.CompletedAt == null);
    }

    [Fact]
    public async Task GetActiveAsync_EmptyStore_ReturnsEmpty()
    {
        var result = await _repository.GetActiveAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_MultipleSagaTypes_IsolatedByType()
    {
        // Save via our OrderSaga repository
        var orderSaga = CreateSaga(correlationId: "shared-corr");
        await _repository.SaveAsync(orderSaga);

        // Manually insert a different saga type with the same correlation
        _dbContext.SagaInstances.Add(new SagaInstance
        {
            Id = Guid.NewGuid(),
            SagaType = "SomeOtherSaga",
            CorrelationId = "shared-corr",
            State = 0,
            Status = SagaStatus.Active,
            StartedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // The repository should only find the OrderSaga instance
        var found = await _repository.FindByCorrelationAsync("shared-corr");
        found.Should().NotBeNull();
        found!.Id.Should().Be(orderSaga.Id);

        // Total instances in DB should be 2
        var totalInstances = await _dbContext.SagaInstances.CountAsync();
        totalInstances.Should().Be(2);
    }
}
