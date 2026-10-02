using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;
using Xunit;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     The outbox captures the ambient W3C trace context at write time
///     (<see cref="EventOutboxEntry.TraceParent"/>) so asynchronous delivery can re-attach to the
///     originating distributed trace across the async boundary.
/// </summary>
public class EventOutboxTraceContextTests
{
    private sealed class OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : DbContext(options)
    {
        public DbSet<TestDbEntity> Entities => Set<TestDbEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestDbEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Ignore(x => x.DomainEvents);
            });
            modelBuilder.ApplyConfiguration(new EventOutboxEntryConfiguration());
        }
    }

    [Fact]
    public async Task Interceptor_CapturesAmbientTraceParent_OntoOutboxEntry()
    {
        // An ActivityListener that samples makes StartActivity return a live Activity.
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("trace-context-test");

        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new EventOutboxInterceptor())
            .Options;

        using var ctx = new OutboxTestDbContext(options);

        using var activity = source.StartActivity("incoming-request");
        activity.Should().NotBeNull("the listener samples so an ambient Activity exists");

        var entity = new TestDbEntity { Name = "Initial" };
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();   // no domain event yet (just added)

        entity.ChangeName("Updated");   // raises a TestDomainEvent
        await ctx.SaveChangesAsync();   // interceptor writes the outbox entry

        var entry = ctx.Set<EventOutboxEntry>().Single();
        entry.TraceParent.Should().Be(activity!.Id,
            "the ambient trace id must be captured so async delivery re-attaches to the trace");
    }

    [Fact]
    public async Task Interceptor_NoAmbientActivity_LeavesTraceParentNull()
    {
        // No ActivityListener registered → Activity.Current is null → TraceParent stays null.
        Activity.Current.Should().BeNull();

        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new EventOutboxInterceptor())
            .Options;

        using var ctx = new OutboxTestDbContext(options);

        var entity = new TestDbEntity { Name = "Initial" };
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();
        entity.ChangeName("Updated");
        await ctx.SaveChangesAsync();

        ctx.Set<EventOutboxEntry>().Single().TraceParent.Should().BeNull();
    }
}
