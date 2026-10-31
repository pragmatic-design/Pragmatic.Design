using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Audit.EFCore;
using Pragmatic.Persistence.EFCore.Auditing;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     An <c>[Audited]</c> save files its entry in the segment the application's registered <see cref="IAuditSegmentNaming" /> names, so the
///     sealer, which closes segments by that same naming, seals it. The interceptor built its own hourly naming: an application with a daily
///     one had entries filed under hourly ids its sealer could not read.
/// </summary>
public class AnAuditedSaveFilesItsEntryByTheRegisteredNamingTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheEntry_IsInTheRegisteredNamingsSegment_AndTheSealerSealsIt()
    {
        var clock = new FakeTimeProvider(Morning);
        await using var provider = Services(clock);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ThingContext>();
            context.Things.Add(new AuditedThing { Id = Guid.NewGuid(), Name = "kept" });
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var entry = await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Entries.SingleAsync();
            entry.SegmentId.Should().Be("day-2026-10-31", "the registered naming files the entry, not a naming of the interceptor's own");
        }

        clock.Advance(TimeSpan.FromDays(2));
        await using (var scope = provider.CreateAsyncScope())
        {
            var sealedIds = await scope.ServiceProvider.GetRequiredService<IAuditSealer>().SealDueSegmentsAsync();
            sealedIds.Should().Equal(["day-2026-10-31"]);
        }
    }

    /// <summary>The trail as an application registers it, with a daily naming of its own, and an [Audited] entity's context.</summary>
    private static ServiceProvider Services(TimeProvider clock)
    {
        var database = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton<IAuditSegmentNaming>(new DailyNaming());
        services.AddAuditTrail();
        services.AddDbContext<AuditDbContext>(o => o.UseInMemoryDatabase(database));
        services.AddDbContext<ThingContext>((sp, o) => o.UseInMemoryDatabase(database)
            .AddInterceptors(new AuditLogInterceptor(sp.GetRequiredService<TimeProvider>(), null)));
        return services.BuildServiceProvider();
    }

    private sealed class AuditedThing : IAuditedEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class ThingContext(DbContextOptions<ThingContext> options) : DbContext(options)
    {
        public DbSet<AuditedThing> Things => Set<AuditedThing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditedThing>(b => b.HasKey(e => e.Id));
            AuditDbContext.ApplyAuditConfigurations(modelBuilder);
        }
    }

    /// <summary>One segment per UTC day, <c>day-yyyy-MM-dd</c>: an id it did not produce is not one of its windows.</summary>
    private sealed class DailyNaming : IAuditSegmentNaming
    {
        private const string Format = "'day-'yyyy-MM-dd";

        public TimeSpan GracePeriod => TimeSpan.FromMinutes(5);

        public string SegmentFor(DateTimeOffset occurredAt) => occurredAt.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

        public DateTimeOffset WindowEnd(string segmentId)
            => DateTimeOffset.TryParseExact(segmentId, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
                ? start.AddDays(1)
                : throw new FormatException($"'{segmentId}' is not a segment id of the daily naming.");
    }
}
