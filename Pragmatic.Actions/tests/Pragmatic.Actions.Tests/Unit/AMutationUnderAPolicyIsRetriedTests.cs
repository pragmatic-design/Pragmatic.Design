using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Commit;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Repository;
using Pragmatic.Resilience;
using Pragmatic.Resilience.Bridge;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     A mutation under <c>[ResiliencePolicy]</c> runs its attempts inside the pipeline, and each attempt
///     after the first starts from a unit of work that forgot the failed one.
/// </summary>
/// <remarks>
///     <para>
///         The invoker here is written by hand in the shape the generator emits for the attribute
///         (<c>AMutationRunsInsideItsPolicyTests</c> pins that shape): <c>RunAttemptsAsync</c> over the
///         named pipeline. The pipeline and the change tracker are real — a retry policy from
///         Pragmatic.Resilience, an EF Core context — because the defect the retry can introduce is in
///         the tracker: a failed save leaves the children it added pending, and a second attempt that
///         writes them again duplicates every one.
///     </para>
/// </remarks>
public sealed class AMutationUnderAPolicyIsRetriedTests
{
    private const string Policy = "orders";

    public sealed class Order
    {
        public Guid Id { get; set; }
        public int Revision { get; set; }
        public List<OrderLine> Lines { get; set; } = [];
    }

    /// <remarks>
    ///     ⚠️ The key to the order is optional on purpose. With a required one EF Core forgets the lines
    ///     itself when the failed save detaches their order — an orphan of a required relationship is
    ///     dropped — and the duplication this suite guards against does not happen. Measured: 2 lines
    ///     with <c>Guid</c>, 4 with <c>Guid?</c>, without <c>DiscardChanges</c>.
    /// </remarks>
    public sealed class OrderLine
    {
        public Guid Id { get; set; }
        public Guid? OrderId { get; set; }
        public string Sku { get; set; } = "";
    }

    private sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderLine> Lines => Set<OrderLine>();
    }

    /// <summary>
    ///     Writes an order: a new revision, and on a new order two lines. The body throws — after it has
    ///     changed the row — on the attempts it is told to.
    /// </summary>
    private sealed class WriteOrder : Mutation<Order>
    {
        public Guid OrderId { get; init; }
        public int FailingApplies { get; init; }
        public int Applies { get; private set; }

        public override Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
        {
            entity.Revision++;

            if (++Applies <= FailingApplies)
                throw new InvalidOperationException($"Transient failure on attempt {Applies}.");

            if (OrderId == Guid.Empty)
            {
                entity.Lines.Add(new OrderLine { Sku = "A" });
                entity.Lines.Add(new OrderLine { Sku = "B" });
            }

            return Task.FromResult<Result<Order, IError>>(entity);
        }
    }

    /// <summary>The unit of work the generated invoker gets, over a real change tracker.</summary>
    private sealed class ShopUnitOfWork(ShopDb db) : IUnitOfWork
    {
        public int Discards { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

        public void Detach(object entity) => db.Entry(entity).State = EntityState.Detached;

        public void DiscardChanges()
        {
            Discards++;
            db.ChangeTracker.Clear();
        }

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No transaction in these tests.");

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Refuses the first <paramref name="failures" /> saves, as a dropped connection would.</summary>
    private sealed class FailingSaves(int failures) : SaveChangesInterceptor
    {
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ++_saves <= failures
                ? throw new DbUpdateException($"Transient failure on save {_saves}.")
                : ValueTask.FromResult(result);
    }

    private sealed class OrderInvoker(
        IServiceProvider serviceProvider,
        ShopDb db,
        ShopUnitOfWork unitOfWork,
        IResiliencePipelineProvider? resilience)
        : MutationInvoker<WriteOrder, Order>(serviceProvider)
    {
        protected override void InjectDependencies(WriteOrder mutation) { }

        // A query, as the generated load is: identity resolution hands back the tracked instance.
        protected override Task<Order?> LoadEntityAsync(WriteOrder mutation, CancellationToken ct)
            => db.Orders.FirstOrDefaultAsync(o => o.Id == mutation.OrderId, ct);

        protected override Order CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.CreateOrUpdate;
        protected override bool HasEntityId(WriteOrder mutation) => mutation.OrderId != Guid.Empty;
        protected override string? GetEntityIdString(WriteOrder mutation) => mutation.OrderId.ToString();
        protected override void PersistNew(Order entity) => db.Orders.Add(entity);
        protected override void DeleteEntity(Order entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        // What the generator emits for [ResiliencePolicy("orders")].
        protected override async Task<Result<Order, IError>> RunAttemptsAsync(
            Func<CancellationToken, Task<Result<Order, IError>>> attempt, CancellationToken ct)
        {
            if (resilience is null)
                return await base.RunAttemptsAsync(attempt, ct).ConfigureAwait(false);

            var pipeline = resilience.GetPipeline(Policy);
            try
            {
                return await pipeline.ExecuteAsync(
                    (ctx, innerCt) => attempt(innerCt),
                    new ResilienceContext { OperationName = nameof(WriteOrder) },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ResilienceResultBridge.TryMapToError(ex, out var error))
            {
                return Result<Order, IError>.Failure(error);
            }
        }
    }

    private sealed class Shop : IDisposable
    {
        private readonly ServiceProvider _services;

        public Shop(bool withPolicy, int failingSaves = 0)
        {
            var services = new ServiceCollection();
            services.AddSingleton(Options.Create(new ValidationOptions()));
            services.AddPragmaticResilience();
            _services = services.BuildServiceProvider();

            var registry = _services.GetRequiredService<IResiliencePipelineRegistry>();
            registry.AddPolicy(Policy, b => b.AddRetry(o =>
            {
                o.MaxRetries = 2;
                o.BaseDelay = TimeSpan.Zero;
                o.UseJitter = false;
            }));

            DatabaseName = Guid.NewGuid().ToString();
            Db = new ShopDb(new DbContextOptionsBuilder<ShopDb>()
                .UseInMemoryDatabase(DatabaseName)
                .AddInterceptors(new FailingSaves(failingSaves))
                .Options);
            UnitOfWork = new ShopUnitOfWork(Db);
            Invoker = new OrderInvoker(_services, Db, UnitOfWork, withPolicy ? registry : null);
        }

        private string DatabaseName { get; }
        public ShopDb Db { get; }
        public ShopUnitOfWork UnitOfWork { get; }
        public OrderInvoker Invoker { get; }

        /// <summary>An order already in the store, written through another context.</summary>
        public Guid Seed()
        {
            using var fresh = Fresh();
            var order = new Order();
            fresh.Orders.Add(order);
            fresh.SaveChanges();
            return order.Id;
        }

        /// <summary>The stored revision of an order.</summary>
        public int StoredRevision(Guid id)
        {
            using var fresh = Fresh();
            return fresh.Orders.Single(o => o.Id == id).Revision;
        }

        /// <summary>The orders the store holds, read through a context that tracked nothing.</summary>
        public int StoredOrders()
        {
            using var fresh = Fresh();
            return fresh.Orders.Count();
        }

        /// <summary>The order lines the store holds, read the same way.</summary>
        public int StoredLines()
        {
            using var fresh = Fresh();
            return fresh.Lines.Count();
        }

        private ShopDb Fresh()
            => new(new DbContextOptionsBuilder<ShopDb>().UseInMemoryDatabase(DatabaseName).Options);

        public void Dispose()
        {
            Db.Dispose();
            _services.Dispose();
        }
    }

    [Fact]
    public async Task ABodyThatFailsOnce_IsRunAgain_AndTheMutationSucceeds()
    {
        using var shop = new Shop(withPolicy: true);
        var mutation = new WriteOrder { FailingApplies = 1 };

        var result = await shop.Invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        mutation.Applies.Should().Be(2, "the policy retries, so the body runs a second time");
        shop.StoredOrders().Should().Be(1);
        shop.StoredLines().Should().Be(2);
    }

    /// <summary>
    ///     A save refused once: the retry writes one order and its two lines, not the first attempt's
    ///     lines as well.
    /// </summary>
    [Fact]
    public async Task ASaveThatFailsOnce_IsRetried_WithoutDuplicatingTheChildren()
    {
        using var shop = new Shop(withPolicy: true, failingSaves: 1);
        var mutation = new WriteOrder();

        var result = await shop.Invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        mutation.Applies.Should().Be(2);
        shop.StoredLines().Should().Be(2,
            "the failed attempt's lines were forgotten, so only the second attempt's are written");
        shop.StoredOrders().Should().Be(1);
        shop.UnitOfWork.Discards.Should().Be(1, "only the attempt after the failure starts clean");
    }

    /// <summary>
    ///     An update whose body changed the row and then failed: the retry reads the row again instead
    ///     of applying a second time over the first attempt's change.
    /// </summary>
    /// <remarks>
    ///     The load is a query, and a query returns the instance the context already tracks without
    ///     overwriting it — so without a clean start the second attempt increments a revision the first
    ///     one had already incremented.
    /// </remarks>
    [Fact]
    public async Task AnUpdateThatFailsAfterChangingTheRow_IsRetriedFromTheStoredRow()
    {
        using var shop = new Shop(withPolicy: true);
        var id = shop.Seed();
        var mutation = new WriteOrder { OrderId = id, FailingApplies = 1 };

        var result = await shop.Invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        mutation.Applies.Should().Be(2);
        shop.StoredRevision(id).Should().Be(1, "one successful write, one new revision");
    }

    /// <summary>Retries exhausted: the mutation answers with the resilience error, not an exception.</summary>
    [Fact]
    public async Task ABodyThatNeverSucceeds_EndsInAFailure_AfterEveryAttempt()
    {
        using var shop = new Shop(withPolicy: true);
        var mutation = new WriteOrder { FailingApplies = int.MaxValue };

        var result = await shop.Invoker.InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue("an exhausted retry is the operation's failure, not a raw 500");
        mutation.Applies.Should().Be(3, "the first attempt and two retries");
        shop.StoredOrders().Should().Be(0);
    }

    /// <summary>The control: without the policy the body runs once and its failure propagates.</summary>
    [Fact]
    public async Task WithoutAPolicy_TheBodyRunsOnce()
    {
        using var shop = new Shop(withPolicy: false);
        var mutation = new WriteOrder { FailingApplies = 1 };

        var act = async () => await shop.Invoker.InvokeAsync(mutation).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
        mutation.Applies.Should().Be(1);
        shop.UnitOfWork.Discards.Should().Be(0);
    }

    /// <summary>
    ///     Nested in an invocation that holds the same unit of work, the mutation stages its writes for
    ///     the owner: there is no save of its own to retry, so it runs once.
    /// </summary>
    [Fact]
    public async Task InsideAnotherInvocationsCommit_TheBodyRunsOnce()
    {
        using var shop = new Shop(withPolicy: true);
        var mutation = new WriteOrder { FailingApplies = 1 };
        using var owner = CommitScope.Claim(shop.UnitOfWork, CommitMode.Once);

        var act = async () => await shop.Invoker.InvokeAsync(mutation).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
        mutation.Applies.Should().Be(1, "the retry belongs to the invocation that commits");
        shop.UnitOfWork.Discards.Should().Be(0, "the owner's tracked writes are not this mutation's to forget");
    }
}
