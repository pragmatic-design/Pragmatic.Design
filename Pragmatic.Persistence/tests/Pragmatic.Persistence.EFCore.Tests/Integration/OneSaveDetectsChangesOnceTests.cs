using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Events;
using Pragmatic.Events.EFCore;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Messaging.EFCore.Outbox;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Auditing;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.EFCore.RollUp;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.RollUp;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     One <c>SaveChanges</c> runs one change-detection pass before EF's own, however many interceptors read
///     the tracker and from whichever assembly they come (#137). Passes are counted on EF's
///     <c>DetectChangesStarting</c> event, which every pass raises, whoever asked for it.
/// </summary>
/// <remarks>
///     The chain is every framework interceptor that reads the tracker: Persistence.EFCore's seven,
///     Events.EFCore's two and the messaging outbox, with a signed-in caller in a resolved tenant so that none
///     of them returns early. Before the change a save through it ran eleven passes.
/// </remarks>
public sealed class OneSaveDetectsChangesOnceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PassCounter _passes = new();

    public OneSaveDetectsChangesOnceTests()
    {
        _connection.Open();
        using var context = new TicketContext(Options(_connection, _passes).Options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void ASave_ThroughEveryInterceptor_DetectsOnceBeforeEfCoresOwnPass()
    {
        var tickets = Seed(3);
        using var context = new TicketContext(Options(_connection, _passes).AddInterceptors(Chain()).Options);
        context.Tickets.AttachRange(tickets);
        foreach (var ticket in tickets)
            ticket.Title = "Changed";

        _passes.Reset();
        context.SaveChanges();

        _passes.Count.Should().Be(2);
        StampedRows().Should().Be(3);
    }

    [Fact]
    public async Task AnAsyncSave_ThroughEveryInterceptor_DetectsOnceBeforeEfCoresOwnPass()
    {
        var tickets = Seed(3);
        using var context = new TicketContext(Options(_connection, _passes).AddInterceptors(Chain()).Options);
        context.Tickets.AttachRange(tickets);
        foreach (var ticket in tickets)
            ticket.Title = "Changed";

        _passes.Reset();
        await context.SaveChangesAsync();

        _passes.Count.Should().Be(2);
        StampedRows().Should().Be(3);
    }

    [Fact]
    public void ASecondSave_OnTheSameContext_DetectsAgain()
    {
        var tickets = Seed(1);
        using var context = new TicketContext(Options(_connection, _passes).AddInterceptors(Chain()).Options);
        context.Tickets.AttachRange(tickets);
        tickets[0].Title = "First";
        context.SaveChanges();

        // Changed through the property alone: only a change-detection pass can tell the interceptors.
        tickets[0].Title = "Second";
        _passes.Reset();
        context.SaveChanges();

        _passes.Count.Should().Be(2, "the claim belongs to one save, not to the context");
    }

    [Fact]
    public async Task AUnitOfWork_ThatFillsGeneratedValues_DetectsOnceForTheWholeSave()
    {
        using var context = new TicketContext(Options(_connection, _passes).AddInterceptors(Chain()).Options);
        context.Tickets.Add(NewTicket());
        IUnitOfWork unitOfWork = new EfCoreUnitOfWork(
            context, dispatcher: new IgnoringDispatcher(), generatedValues: [new TicketNumberBinding()]);

        _passes.Reset();
        await unitOfWork.SaveChangesAsync();

        // The fill's pass, then EF's own. Nothing in the interceptors, and nothing after the save.
        _passes.Count.Should().Be(2);
        context.Tickets.Single().Number.Should().Be("T-1");
    }

    [Fact]
    public void AParentTheRollUpChanges_IsSeenModifiedByTheInterceptorsAfterIt()
    {
        // In memory, so the roll-up takes its tracked path and writes the parent's CLR property.
        var options = new DbContextOptionsBuilder<TicketContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(Chain())
            .Options;
        var project = new Project { Id = Guid.NewGuid() };
        using (var seed = new TicketContext(options))
        {
            seed.Projects.Add(project);
            seed.SaveChanges();
        }

        using var context = new TicketContext(options);
        var tracked = context.Projects.Single();
        context.Tickets.Add(NewTicket(project.Id, amount: 5m));

        context.SaveChanges();

        tracked.Total.Should().Be(5m);
        tracked.Raised.Should().Equal(EntityLifecycle.Updated);
    }

    private Ticket[] Seed(int count)
    {
        var tickets = Enumerable.Range(0, count).Select(_ => NewTicket()).ToArray();
        using var context = new TicketContext(Options(_connection, _passes).Options);
        context.Tickets.AddRange(tickets);
        context.SaveChanges();
        return tickets;
    }

    private int StampedRows()
    {
        using var context = new TicketContext(Options(_connection, _passes).Options);
        return context.Tickets.Count(t => t.UpdatedBy == "clerk");
    }

    private static Ticket NewTicket(Guid? projectId = null, decimal amount = 1m) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId ?? Guid.NewGuid(),
        Title = "New",
        Amount = amount,
        TenantId = ResolvedTenant.Id,
    };

    private static DbContextOptionsBuilder<TicketContext> Options(SqliteConnection connection, PassCounter passes)
        => new DbContextOptionsBuilder<TicketContext>()
            .UseSqlite(connection)
            .LogTo(passes.Log, [CoreEventId.DetectChangesStarting]);

    private static IInterceptor[] Chain()
    {
        var user = FakeCurrentUser.Authenticated("clerk");
        var time = TimeProvider.System;
        return
        [
            new AuditingInterceptor(time, user),
            new SoftDeleteInterceptor(time, user),
            new OwnershipInterceptor(user),
            new ScopeInterceptor(user),
            new TenantInterceptor(new ResolvedTenant()),
            new RollUpInterceptor([TicketAmounts()]),
            new LifecycleEventsInterceptor(),
            new AuditLogInterceptor(time, user),
            new EventOutboxInterceptor(),
            new OutboxInterceptor(),
        ];
    }

    private static RollUpRule<Ticket, Project> TicketAmounts() => new()
    {
        AggregatePropertyName = nameof(Project.Total),
        ParentKey = t => t.ProjectId,
        Amount = t => t.Amount,
        ApplyToParent = (project, delta) => project.ApplyDelta(delta),
    };

    private sealed class Ticket : IAuditable, ISoftDelete, ITenantEntity
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string Title { get; set; } = "";
        public string? Number { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
        public string TenantId { get; set; } = "";
    }

    private sealed class Project : IRaisesLifecycleEvents
    {
        public Guid Id { get; set; }
        public decimal Total { get; private set; }
        public List<EntityLifecycle> Raised { get; } = [];

        public void ApplyDelta(decimal delta) => Total += delta;
        public void RaiseLifecycleEvents(EntityLifecycle lifecycle) => Raised.Add(lifecycle);
    }

    private sealed class TicketContext(DbContextOptions<TicketContext> options) : DbContext(options)
    {
        public DbSet<Ticket> Tickets => Set<Ticket>();
        public DbSet<Project> Projects => Set<Project>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Ticket>().HasKey(t => t.Id);
            modelBuilder.Entity<Ticket>().Property(t => t.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<Project>().HasKey(p => p.Id);
            modelBuilder.Entity<Project>().Property(p => p.Total).HasPrecision(18, 2);
            modelBuilder.Entity<Project>().Ignore(p => p.Raised);
        }
    }

    private sealed class PassCounter
    {
        public int Count { get; private set; }
        public void Log(string message) => Count++;
        public void Reset() => Count = 0;
    }

    private sealed class ResolvedTenant : ITenantContext
    {
        public const string Id = "tenant-a";
        public string? TenantId => Id;
        public string? TenantName => "Tenant A";
        public bool IsResolved => true;
    }

    private sealed class TicketNumberBinding : IGeneratedValueBinding
    {
        public Type EntityType => typeof(Ticket);
        public string PropertyName => nameof(Ticket.Number);

        public Task<bool> TryFillAsync(object entity, LifecycleContext context, CancellationToken ct)
        {
            if (entity is not Ticket { Number: null } ticket)
                return Task.FromResult(false);
            ticket.Number = "T-1";
            return Task.FromResult(true);
        }
    }

    private sealed class IgnoringDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent
            => Task.CompletedTask;

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
