using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     An application can run one delivery pass on demand, without waiting for the loop.
/// </summary>
/// <remarks>
///     <para>
///         The delivery pass existed and was <c>internal</c>, reachable only from this assembly. An
///         application that turned the outbox on therefore had one way to assert anything about it:
///         wait for a background loop whose interval defaults to five seconds. That is the shape this
///         repository has already paid for — a suite where five runs of seven were red on a
///         thirty-second timeout while the same test passed alone in 78 ms, because going through the
///         hosted service asserts that the scheduler runs promptly on a loaded machine.
///     </para>
///     <para>
///         ⚠️ <b>Generic on the context, deliberately.</b> One registration per <c>DbContext</c> that
///         owns an outbox, so an application with two of them names which one it is draining instead
///         of resolving an interface that silently answers for the last one registered.
///     </para>
/// </remarks>
public sealed class AnApplicationCanDrainTheOutboxItselfTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = PragmaticJsonOptions.Default.Build();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly RecordingDispatcher _dispatcher = new();

    public AnApplicationCanDrainTheOutboxItselfTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<OutboxTestDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IDomainEventDispatcher>(_dispatcher);
        services.AddSingleton<IEventOutboxTypeResolver>(new EventOutboxTypeResolver([typeof(TestDomainEvent)]));
        services.AddLogging();

        // The registration an application writes, and nothing else: what it leaves behind is what a
        // consumer can reach.
        services.AddEventOutbox<OutboxTestDbContext>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreated();
    }

    private IEventOutboxDrainer<OutboxTestDbContext> Drainer()
        => _provider.GetRequiredService<IEventOutboxDrainer<OutboxTestDbContext>>();

    /// <summary>The setpoint: AddEventOutbox leaves a drain an application can call.</summary>
    [Fact]
    public async Task AddEventOutbox_LeavesADrainTheApplicationCanCall()
    {
        var id = SeedEntry(new TestDomainEvent("hello"));

        var delivered = await Drainer().DrainOnceAsync(CancellationToken.None);

        delivered.Should().Be(1, "the pass reports what it delivered, so a caller can assert on it");
        _dispatcher.Dispatched.Should().ContainSingle();
        Reload(id).ProcessedAt.Should().NotBeNull();
    }

    /// <summary>
    ///     ⚠️ The control the story turns on: a handler that throws does not lose the event.
    /// </summary>
    /// <remarks>
    ///     Without this, "the outbox is enabled" is satisfied by an outbox that is written and never
    ///     read. The row stays, the claim is released, and the next pass delivers it — which is the
    ///     whole reason for writing the event down instead of handling it in the transaction.
    /// </remarks>
    [Fact]
    public async Task AHandlerThatThrows_KeepsTheEventForTheNextPass()
    {
        var id = SeedEntry(new TestDomainEvent("retry me"));
        _dispatcher.ThrowOnce = true;

        var failed = await Drainer().DrainOnceAsync(CancellationToken.None);

        failed.Should().Be(0, "nothing was delivered");
        var afterFailure = Reload(id);
        afterFailure.ProcessedAt.Should().BeNull("the event is not lost");
        afterFailure.Attempts.Should().Be(1);
        afterFailure.ClaimedBy.Should().BeNull("the claim is released, or nobody can pick it up again");

        var delivered = await Drainer().DrainOnceAsync(CancellationToken.None);

        delivered.Should().Be(1, "the second pass delivers what the first could not");
        Reload(id).ProcessedAt.Should().NotBeNull();
    }

    /// <summary>
    ///     ⚠️ The second control: the effect happens once, not once per pass.
    /// </summary>
    /// <remarks>
    ///     Delivery is at-least-once by design, and the guard against repeating it is
    ///     <c>ProcessedAt</c>. A pass over an outbox with nothing pending must dispatch nothing —
    ///     otherwise every drain would re-deliver history, and "exactly once after a retry" would be
    ///     satisfied by a store that never marks anything done.
    /// </remarks>
    [Fact]
    public async Task ASecondPass_DeliversNothingAgain()
    {
        SeedEntry(new TestDomainEvent("once"));

        (await Drainer().DrainOnceAsync(CancellationToken.None)).Should().Be(1);
        (await Drainer().DrainOnceAsync(CancellationToken.None)).Should().Be(0,
            "an entry already processed is not eligible");

        _dispatcher.Dispatched.Should().ContainSingle("the handler saw it once");
    }

    /// <summary>⚠️ And the control on all three: an empty outbox is a no-op, not a failure.</summary>
    [Fact]
    public async Task AnEmptyOutbox_DrainsToNothing()
    {
        (await Drainer().DrainOnceAsync(CancellationToken.None)).Should().Be(0);
        _dispatcher.Dispatched.Should().BeEmpty();
    }

    private Guid SeedEntry(IDomainEvent domainEvent)
    {
        using var scope = _provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        var entry = new EventOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = domainEvent.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(domainEvent, JsonOptions.GetTypeInfo(domainEvent.GetType())),
            OccurredAt = domainEvent.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Add(entry);
        ctx.SaveChanges();
        return entry.Id;
    }

    private EventOutboxEntry Reload(Guid id)
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
            .Set<EventOutboxEntry>().AsNoTracking().Single(e => e.Id == id);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    ///     A dispatcher that can fail exactly once, which is what a retry needs to be measurable.
    /// </summary>
    /// <remarks>
    ///     The sibling suite's recorder throws until it is told to stop; here the first pass has to
    ///     fail and the second has to succeed, or "the event is not lost" cannot be told apart from
    ///     "the event is never delivered".
    /// </remarks>
    private sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Dispatched { get; } = [];

        public bool ThrowOnce { get; set; }

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
            => DispatchAsync([@event], ct);

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            if (ThrowOnce)
            {
                ThrowOnce = false;
                throw new InvalidOperationException("handler failed");
            }

            Dispatched.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
