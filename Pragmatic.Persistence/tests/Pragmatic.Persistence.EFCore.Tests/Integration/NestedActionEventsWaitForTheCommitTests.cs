using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Events;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     The events of an action invoked inside a <c>[Transactional]</c> one are dispatched after the outer commit, and not at
///     all when the outer one rolls back.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The action invokers dispatched what they raised straight after their own save, or their own savepoint's
///         release, whoever owned the commit. Nested in a transactional action that then failed, the handlers had run for
///         writes the rollback undid. A nested mutation already deferred its events into the owner's batch.
///     </para>
///     <para>
///         The invokers are written by hand, the way the generated ones are, over a real unit of work on SQLite. The
///         dispatcher records each event and whether a transaction was still open when it arrived.
///     </para>
/// </remarks>
public sealed class NestedActionEventsWaitForTheCommitTests : IDisposable
{
    private readonly TestDbContext _db;
    private readonly EfCoreUnitOfWork _unitOfWork;
    private readonly RecordingDispatcher _dispatcher;
    private readonly IServiceProvider _services;

    public NestedActionEventsWaitForTheCommitTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite("DataSource=:memory:").Options;
        _db = new TestDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _unitOfWork = new EfCoreUnitOfWork(_db);
        _dispatcher = new RecordingDispatcher(_db);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDomainEventDispatcher>(_dispatcher);
        _services = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVoidActionNestedInATransactionalOneThatFails_DispatchesNothing(bool innerTransactional)
    {
        var result = await Outer(Inner(innerTransactional), outerSucceeds: false);

        result.IsFailure.Should().BeTrue();
        StoredNames().Should().BeEmpty("the outer action rolled back the inner one's write");
        _dispatcher.Dispatched.Should().BeEmpty("the event announces a write that no longer exists");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVoidActionNestedInATransactionalOneThatSucceeds_DispatchesOnce_AfterTheCommit(bool innerTransactional)
    {
        var result = await Outer(Inner(innerTransactional), outerSucceeds: true);

        result.IsSuccess.Should().BeTrue();
        StoredNames().Should().BeEquivalentTo(["inner"]);
        _dispatcher.Dispatched.Should().BeEquivalentTo([new Dispatch("inner", InsideATransaction: false)]);
    }

    [Fact]
    public async Task AnActionWithAResultNestedInATransactionalOneThatFails_DispatchesNothing()
    {
        var result = await Outer(InnerWithResult(), outerSucceeds: false);

        result.IsFailure.Should().BeTrue();
        StoredNames().Should().BeEmpty("the outer action rolled back the inner one's write");
        _dispatcher.Dispatched.Should().BeEmpty("the event announces a write that no longer exists");
    }

    [Fact]
    public async Task AnActionWithAResultNestedInATransactionalOneThatSucceeds_DispatchesOnce_AfterTheCommit()
    {
        var result = await Outer(InnerWithResult(), outerSucceeds: true);

        result.IsSuccess.Should().BeTrue();
        StoredNames().Should().BeEquivalentTo(["inner"]);
        _dispatcher.Dispatched.Should().BeEquivalentTo([new Dispatch("inner", InsideATransaction: false)]);
    }

    private Func<CancellationToken, Task> Inner(bool transactional)
    {
        var inner = new VoidInvoker(_services, _unitOfWork, transactional, raises: "inner");
        return ct => inner.InvokeAsync(new VoidWork(async innerCt =>
        {
            await Write("inner", innerCt).ConfigureAwait(false);
            return true;
        }), ct);
    }

    private Func<CancellationToken, Task> InnerWithResult()
    {
        var inner = new ResultInvoker(_services, _unitOfWork, raises: "inner");
        return ct => inner.InvokeAsync(new ResultWork(async innerCt =>
        {
            await Write("inner", innerCt).ConfigureAwait(false);
            return true;
        }), ct);
    }

    private Task<VoidResult<IError>> Outer(Func<CancellationToken, Task> inner, bool outerSucceeds)
    {
        var outer = new VoidInvoker(_services, _unitOfWork, transactional: true, raises: null);
        return outer.InvokeAsync(new VoidWork(async ct =>
        {
            await inner(ct).ConfigureAwait(false);
            return outerSucceeds;
        }));
    }

    private async Task Write(string name, CancellationToken ct)
    {
        _db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = name });
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private List<string> StoredNames()
    {
        _db.ChangeTracker.Clear();
        return [.. _db.Set<TestProduct>().AsNoTracking().Select(p => p.Name)];
    }

    public void Dispose() => _db.Dispose();

    private sealed record TestError(string Code) : IError
    {
        public int StatusCode => 500;
        public string Title => Code;
    }

    private sealed record WorkDone(string Name) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed record Dispatch(string Name, bool InsideATransaction);

    /// <summary>Records each event, and whether the unit of work still had a transaction open when it arrived.</summary>
    private sealed class RecordingDispatcher(TestDbContext db) : IDomainEventDispatcher
    {
        public List<Dispatch> Dispatched { get; } = [];

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
            => DispatchAsync([@event], ct);

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            foreach (var @event in events)
                Dispatched.Add(new Dispatch(((WorkDone)@event).Name, db.Database.CurrentTransaction is not null));
            return Task.CompletedTask;
        }
    }

    /// <summary>A body that writes, then says whether the action succeeded.</summary>
    private sealed class VoidWork(Func<CancellationToken, Task<bool>> body) : VoidDomainAction
    {
        public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => await body(ct).ConfigureAwait(false) ? VoidResult<IError>.Success() : VoidResult<IError>.Failure(new TestError("FAILED"));
    }

    /// <summary>The same, for an action that returns a value.</summary>
    private sealed class ResultWork(Func<CancellationToken, Task<bool>> body) : DomainAction<int>
    {
        public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
            => await body(ct).ConfigureAwait(false) ? 1 : Result<int, IError>.Failure(new TestError("FAILED"));
    }

    /// <summary>Stands in for the generated invoker of a void action that declares <c>[Raises&lt;WorkDone&gt;]</c>.</summary>
    private sealed class VoidInvoker(IServiceProvider services, IUnitOfWork unitOfWork, bool transactional, string? raises)
        : VoidDomainActionInvoker<VoidWork>(services)
    {
        protected override void InjectDependencies(VoidWork action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override bool IsTransactional => transactional;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);

        protected override IReadOnlyList<IDomainEvent> CollectRaisedEvents(VoidWork action)
            => raises is null ? [] : [new WorkDone(raises)];
    }

    /// <summary>Stands in for the generated invoker of an action with a result that declares <c>[Raises&lt;WorkDone&gt;]</c>.</summary>
    private sealed class ResultInvoker(IServiceProvider services, IUnitOfWork unitOfWork, string raises)
        : DomainActionInvoker<ResultWork, int>(services)
    {
        protected override void InjectDependencies(ResultWork action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);

        protected override IReadOnlyList<IDomainEvent> CollectRaisedEvents(ResultWork action, int result) => [new WorkDone(raises)];
    }
}
