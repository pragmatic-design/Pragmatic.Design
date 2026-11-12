using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Caching;
using Pragmatic.Events;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The <c>[InvalidatesCache]</c> of an operation nested in an action that owns the commit runs once, after that commit, and not
///     at all when the owner fails (#707).
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A nested mutation deferred its entity and its events into the owner's batch, and its comment said the invalidation was
///         deferred too, but nothing recorded it: the owner flushed the events and invalidated only its own declaration, so a
///         mutation used as a step never invalidated its entries. A nested action invalidated in its own post-commit block, which for
///         an invocation that does not own the commit is before the owner saves: a read in between repopulates the entry with the
///         state the commit then changes.
///     </para>
///     <para>
///         The invokers are written by hand, the way the generated ones are, over a unit of work that counts its saves; the cache
///         records how many saves had happened when each invalidation arrived.
///     </para>
/// </remarks>
public class ANestedOperationInvalidatesAfterTheOwnersCommitTests
{
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingCache _cache;
    private readonly ServiceProvider _services;

    public ANestedOperationInvalidatesAfterTheOwnersCommitTests()
    {
        _cache = new RecordingCache(_unitOfWork);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<ICacheStack>(_cache);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AMutationNestedInAnAction_InvalidatesOnce_AfterTheOwnersCommit()
    {
        var result = await Owner(ct => new RenameInvoker(_services, _unitOfWork).InvokeAsync(new RenameMutation(), ct), succeeds: true);

        result.IsSuccess.Should().BeTrue();
        _cache.Invalidations.Should().Equal([new Invalidation("renamed", SavesSoFar: 1)],
            "the step's entries are stale once the owner has saved, and only then");
    }

    [Fact]
    public async Task AnActionNestedInAnAction_InvalidatesOnce_AfterTheOwnersCommit()
    {
        var result = await Owner(ct => new PurgeInvoker(_services, _unitOfWork).InvokeAsync(new PurgeAction(), ct), succeeds: true);

        result.IsSuccess.Should().BeTrue();
        _cache.Invalidations.Should().Equal([new Invalidation("purged", SavesSoFar: 1)],
            "before the owner's save a read puts the old state back in the entry, and it outlives the commit");
    }

    /// <summary>The control: the owner fails, nothing is committed, and nothing is stale.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOwnerThatFails_InvalidatesNothing(bool nestedIsAMutation)
    {
        Func<CancellationToken, Task> nested = nestedIsAMutation
            ? ct => new RenameInvoker(_services, _unitOfWork).InvokeAsync(new RenameMutation(), ct)
            : ct => new PurgeInvoker(_services, _unitOfWork).InvokeAsync(new PurgeAction(), ct);

        var result = await Owner(nested, succeeds: false);

        result.IsFailure.Should().BeTrue();
        _unitOfWork.SaveCount.Should().Be(0);
        _cache.Invalidations.Should().BeEmpty("the owner wrote nothing, so every entry is still right");
    }

    private Task<VoidResult<IError>> Owner(Func<CancellationToken, Task> nested, bool succeeds)
        => new OwnerInvoker(_services, _unitOfWork).InvokeAsync(new OwnerAction(async ct =>
        {
            await nested(ct).ConfigureAwait(false);
            return succeeds;
        }));

    private sealed record Invalidation(string Tag, int SavesSoFar);

    private sealed class Row
    {
        public string Name { get; set; } = "";
    }

    private sealed class FailedError : IError
    {
        public string Code => "FAILED";
        public int StatusCode => 400;
        public string Title => Code;
    }

    /// <summary>The owner: an action whose body invokes the nested operation, then succeeds or fails.</summary>
    private sealed class OwnerAction(Func<CancellationToken, Task<bool>> body) : VoidDomainAction
    {
        public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => await body(ct).ConfigureAwait(false) ? VoidResult<IError>.Success() : VoidResult<IError>.Failure(new FailedError());
    }

    private sealed class OwnerInvoker(IServiceProvider services, IUnitOfWork unitOfWork) : VoidDomainActionInvoker<OwnerAction>(services)
    {
        protected override void InjectDependencies(OwnerAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>A void action with <c>[InvalidatesCache]</c>, as the generator emits it.</summary>
    private sealed class PurgeAction : VoidDomainAction, ICacheInvalidator
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default) => Task.FromResult(VoidResult<IError>.Success());

        public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default) => cache.InvalidateByTagAsync("purged", ct);
    }

    private sealed class PurgeInvoker(IServiceProvider services, IUnitOfWork unitOfWork) : VoidDomainActionInvoker<PurgeAction>(services)
    {
        protected override void InjectDependencies(PurgeAction action) { }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>A mutation with <c>[InvalidatesCache]</c>, as the generator emits it.</summary>
    private sealed class RenameMutation : Mutation<Row>, ICacheInvalidator
    {
        public override Task<Result<Row, IError>> ApplyAsync(Row entity, CancellationToken ct = default)
        {
            entity.Name = "renamed";
            return Task.FromResult<Result<Row, IError>>(entity);
        }

        public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default) => cache.InvalidateByTagAsync("renamed", ct);
    }

    private sealed class RenameInvoker(IServiceProvider services, IUnitOfWork unitOfWork) : MutationInvoker<RenameMutation, Row>(services)
    {
        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override void InjectDependencies(RenameMutation mutation) { }

        protected override Task<Row?> LoadEntityAsync(RenameMutation mutation, CancellationToken ct) => Task.FromResult<Row?>(null);

        protected override Row CreateEntity() => new();

        protected override MutationMode GetMode() => MutationMode.Create;

        protected override string? GetEntityIdString(RenameMutation mutation) => null;

        protected override void PersistNew(Row entity) { }

        protected override void DeleteEntity(Row entity) { }

        protected override IReadOnlyList<IDomainEvent> CollectRaisedEvents(RenameMutation mutation, Row entity) => [];

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Counts its saves; nothing else.</summary>
    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No test here opens a transaction.");

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Records each tag invalidated and how many saves had happened by then; everything else is inert.</summary>
    private sealed class RecordingCache(CountingUnitOfWork unitOfWork) : ICacheStack
    {
        public List<Invalidation> Invalidations { get; } = [];

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        {
            Invalidations.Add(new Invalidation(tag, unitOfWork.SaveCount));
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        {
            foreach (var tag in tags)
                Invalidations.Add(new Invalidation(tag, unitOfWork.SaveCount));
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => factory(ct);

        public async ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default)
            => (await factory(ct).ConfigureAwait(false)).Value;

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) => new(default(T));

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default) => new((false, default));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string key, CancellationToken ct = default) => ValueTask.CompletedTask;
    }
}
