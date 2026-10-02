using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Extensions;

namespace Pragmatic.Events.Samples.Samples;

/// <summary>
///     Transactional outbox: the interceptor persists raised domain events into <c>__EventOutbox</c>
///     in the same transaction as the entity change (so an event is never lost if the process dies
///     after commit), and a background delivery loop dispatches them asynchronously with at-least-once
///     semantics. This sample runs the full cycle against an in-memory SQLite database.
/// </summary>
public static class OutboxSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Transactional Outbox — capture in-transaction, deliver async");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // A single open connection keeps the in-memory database alive for the sample.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();

        // Step 2: the DbContext resolves the capture interceptor from DI.
        services.AddDbContext<OutboxDbContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetRequiredService<EventOutboxInterceptor>()));

        services.AddInMemoryDomainEvents();
        services.AddScoped<IDomainEventHandler<StockDepleted>, StockDepletedHandler>();

        // Step 3: register the interceptor, options and delivery background service. The fail-closed
        // type allowlist is derived from the registered IDomainEventHandler<T> types. A short polling
        // interval just makes the sample finish quickly.
        services.AddEventOutbox<OutboxDbContext>(o => o.PollingInterval = TimeSpan.FromMilliseconds(100));

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreatedAsync();

        // ── Capture: raising an event and saving writes an outbox row in the same transaction ──
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var product = new Product { Sku = "WIDGET-1", Stock = 3 };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            product.Sell(3); // depletes stock → raises StockDepleted
            await db.SaveChangesAsync();

            var pending = await db.Set<EventOutboxEntry>().CountAsync(e => e.ProcessedAt == null);
            Console.WriteLine($"  Captured: {pending} pending outbox row committed with the entity change.");
        }

        // ── Deliver: run one pass, on demand ──
        //
        // The same pass the background loop runs on a timer. Starting the hosted service, waiting
        // half a second and stopping it works until the machine is busy, and a reader would copy it
        // into their own tests. Asking for the drain says what is being exercised: the delivery, not
        // the scheduler.
        var delivered = await provider
            .GetRequiredService<IEventOutboxDrainer<OutboxDbContext>>()
            .DrainOnceAsync(CancellationToken.None);

        Console.WriteLine($"  One pass delivered {delivered} row(s).");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var processed = await db.Set<EventOutboxEntry>().CountAsync(e => e.ProcessedAt != null);
            Console.WriteLine($"  Delivered: {processed} row marked processed; handler ran '{StockDepletedHandler.LastMessage}'.");
        }

        await provider.DisposeAsync();
        await connection.DisposeAsync();
        Console.WriteLine();
    }

    // ── Minimal domain + persistence for the sample ──────────────────────────────────────────

    public sealed record StockDepleted(string Sku, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

    public sealed class Product : DomainEventSource
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Sku { get; init; } = string.Empty;
        public int Stock { get; set; }

        public void Sell(int quantity)
        {
            Stock -= quantity;
            if (Stock <= 0)
                RaiseEvent(new StockDepleted(Sku, DateTimeOffset.UtcNow));
        }
    }

    public sealed class StockDepletedHandler : IDomainEventHandler<StockDepleted>
    {
        public static string? LastMessage { get; private set; }

        public Task HandleAsync(StockDepleted domainEvent, CancellationToken ct = default)
        {
            LastMessage = $"reorder {domainEvent.Sku}";
            return Task.CompletedTask;
        }
    }

    public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(e =>
            {
                e.HasKey(p => p.Id);
                e.Ignore(p => p.DomainEvents);
            });

            // Step 1: map the __EventOutbox table.
            modelBuilder.AddEventOutbox();
        }
    }
}
