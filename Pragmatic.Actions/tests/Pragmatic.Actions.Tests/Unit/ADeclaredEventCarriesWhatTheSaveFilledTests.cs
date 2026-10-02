using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Events;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     A <c>[Raises&lt;T&gt;]</c> event carries the value a property has <b>after</b> the save.
/// </summary>
/// <remarks>
///     <para>
///         A <c>[GeneratedValue("…{SEQ}…")]</c> property is filled by the unit of work at the top of its
///         save — <c>EfCoreUnitOfWork.SaveChangesAsync</c> calls <c>FillGeneratedValuesAsync</c> first, on
///         purpose, so the connection is free when a sequence is asked for its next value. An event
///         constructed before that carries the empty string, the handler reads <c>""</c>, and nothing
///         says so: the dispatch is post-commit, so by then the row really does have its number.
///     </para>
///     <para>
///         The generated override constructs the event eagerly (<c>MutationInvokerTemplate.cs:360</c>
///         emits <c>=> [new TEvent(entity.Number, …)]</c>), so <i>when the pipeline calls it</i> is the
///         whole question. These invokers reproduce that shape by hand, and the unit of work fills the
///         number where the real one fills it.
///     </para>
///     <para>
///         ⚠️ Not covered here, and no unit test can: that a handler in a running host reads the value.
///         The dispatch is plumbing downstream of the payload, and the payload is where the defect was.
///         An application that meets it can only work around it by reading the row back.
///     </para>
/// </remarks>
public class ADeclaredEventCarriesWhatTheSaveFilledTests
{
    /// <summary>An entity with a number the save fills, and a subject the mutation sets.</summary>
    private sealed class Case
    {
        /// <summary>Stands in for <c>[GeneratedValue("CASE-{SEQ:5}")]</c>: empty until the save.</summary>
        public string Number { get; set; } = "";

        /// <summary>An ordinary property, and the control: the mutation sets it before anything saves.</summary>
        public string Subject { get; set; } = "";
    }

    private sealed record CaseOpened(string Number, string Subject) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class OpenCaseMutation : Mutation<Case>
    {
        public required string Subject { get; init; }

        public override Task<Result<Case, IError>> ApplyAsync(Case entity, CancellationToken ct = default)
        {
            entity.Subject = Subject;
            return Task.FromResult<Result<Case, IError>>(entity);
        }
    }

    /// <summary>
    ///     Fills the generated values on save, as <c>EfCoreUnitOfWork</c> does, and nothing else.
    /// </summary>
    private sealed class FillingUnitOfWork : IUnitOfWork
    {
        private readonly List<Case> _added = [];

        public int SaveCount { get; private set; }

        public void Add(object entity)
        {
            if (entity is Case added)
                _added.Add(added);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;

            // A value already present is never overwritten, as in the real one: a sequence consumes a
            // number every time it is asked.
            foreach (var added in _added.Where(c => c.Number.Length == 0))
                added.Number = $"CASE-{SaveCount:00000}";

            return Task.FromResult(_added.Count);
        }

        public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No test here opens a transaction.");

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Stands in for the generated invoker: <c>CollectRaisedEvents</c> constructs the event from the
    ///     entity's members eagerly, as the template emits it.
    /// </summary>
    private sealed class OpenCaseInvoker(IServiceProvider sp, FillingUnitOfWork unitOfWork)
        : MutationInvoker<OpenCaseMutation, Case>(sp)
    {
        /// <summary>How many times the pipeline asked for the declared events.</summary>
        public int CollectCount { get; private set; }

        /// <summary>The last event constructed — the payload a handler would be given.</summary>
        public CaseOpened? LastCollected { get; private set; }

        protected override IUnitOfWork? UnitOfWork => unitOfWork;

        protected override void InjectDependencies(OpenCaseMutation mutation) { }

        protected override Task<Case?> LoadEntityAsync(OpenCaseMutation mutation, CancellationToken ct)
            => Task.FromResult<Case?>(null);

        protected override Case CreateEntity() => new();

        protected override MutationMode GetMode() => MutationMode.Create;

        protected override string? GetEntityIdString(OpenCaseMutation mutation) => null;

        protected override void PersistNew(Case entity) => unitOfWork.Add(entity);

        protected override void DeleteEntity(Case entity) { }

        protected override IReadOnlyList<IDomainEvent> CollectRaisedEvents(OpenCaseMutation mutation, Case entity)
        {
            CollectCount++;
            LastCollected = new CaseOpened(entity.Number, entity.Subject);
            return [LastCollected];
        }

        protected override Task SaveChangesAsync(CancellationToken ct) => unitOfWork.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task ThePropertyTheSaveFills_ReachesTheEvent()
    {
        var unitOfWork = new FillingUnitOfWork();
        var invoker = new OpenCaseInvoker(BuildServices(), unitOfWork);

        var result = await invoker.InvokeAsync(new OpenCaseMutation { Subject = "A broken boiler" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Number.Should().Be("CASE-00001", "the save is what fills a {SEQ} value");

        Dispatched(invoker).Number.Should().Be("CASE-00001",
            "the declared event is built after the save, so it carries the number the row has — "
            + "built before it, a handler got the empty string and had no reason to suspect why");
    }

    /// <summary>
    ///     The control: a property the mutation set is carried by the same event, unchanged.
    /// </summary>
    /// <remarks>
    ///     Without it, moving the construction later could lose something else and the test above would
    ///     still pass — and "the number is right" is also satisfied by an event built from the row read
    ///     back rather than from the entity in hand.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryProperty_StillReachesTheSameEvent()
    {
        var invoker = new OpenCaseInvoker(BuildServices(), new FillingUnitOfWork());

        (await invoker.InvokeAsync(new OpenCaseMutation { Subject = "A broken boiler" }))
            .IsSuccess.Should().BeTrue();

        Dispatched(invoker).Subject.Should().Be("A broken boiler",
            "the mutation set it, and where the event is constructed does not move it");
    }

    /// <summary>
    ///     A batch that holds back the save too: the entity is not filled when the mutation returns, so
    ///     what is deferred has to be the construction, not the event.
    /// </summary>
    /// <remarks>
    ///     This is the branch that a diagnostic alone would leave wrong, and the reason the construction
    ///     cannot simply move "two lines lower": nobody has saved yet when this invocation ends.
    /// </remarks>
    [Fact]
    public async Task InABatchThatDefersTheSave_TheEventIsBuiltAtTheFlush()
    {
        var unitOfWork = new FillingUnitOfWork();
        var invoker = new OpenCaseInvoker(BuildServices(), unitOfWork);

        using var batch = new BatchContext(unitOfWork, defersSave: true);

        (await invoker.InvokeAsync(new OpenCaseMutation { Subject = "A broken boiler" }))
            .IsSuccess.Should().BeTrue();

        unitOfWork.SaveCount.Should().Be(0, "the batch holds the save back — this is the branch under test");

        // The owner of the batch saves, then flushes: the two calls a CompositeInvoker makes in order.
        await unitOfWork.SaveChangesAsync();

        var deferred = batch.DeferredEvents.OfType<CaseOpened>().Single();
        deferred.Number.Should().Be("CASE-00001", "the flush is the first moment the number exists");
        deferred.Subject.Should().Be("A broken boiler", "and the rest of the payload is unchanged");
    }

    /// <summary>
    ///     The control for the branch above: reading the deferred events twice does not build twice.
    /// </summary>
    /// <remarks>
    ///     A construction performed on every read would call the generated override again per reader,
    ///     and an event whose identity is a fresh <c>Guid</c> would then be a different event each time
    ///     — a dispatcher deduplicating on <c>EventId</c> would stop recognising its own redelivery.
    /// </remarks>
    [Fact]
    public async Task ReadingTheDeferredEventsTwice_BuildsThemOnce()
    {
        var unitOfWork = new FillingUnitOfWork();
        var invoker = new OpenCaseInvoker(BuildServices(), unitOfWork);

        using var batch = new BatchContext(unitOfWork, defersSave: true);

        await invoker.InvokeAsync(new OpenCaseMutation { Subject = "A broken boiler" });
        await unitOfWork.SaveChangesAsync();

        var first = batch.DeferredEvents.OfType<CaseOpened>().Single();
        var second = batch.DeferredEvents.OfType<CaseOpened>().Single();

        invoker.CollectCount.Should().Be(1, "the deferred construction runs once, at the first read");
        second.Should().BeSameAs(first);
    }

    /// <summary>The event the pipeline handed to the post-commit side effects.</summary>
    private static CaseOpened Dispatched(OpenCaseInvoker invoker)
    {
        invoker.CollectCount.Should().Be(1, "the pipeline asks for the declared events exactly once");
        return invoker.LastCollected!;
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
