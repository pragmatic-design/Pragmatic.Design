using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.EFCore.Entities;

namespace Pragmatic.Messaging.EFCore;

/// <summary>
///     EF Core-backed <see cref="IScheduleHandleStore"/> on the <c>__ScheduleHandles</c> table —
///     schedule cancellation survives process restarts. Singleton (the scheduler is a singleton):
///     each operation runs in its own DI scope.
/// </summary>
public sealed class EfCoreScheduleHandleStore(IServiceScopeFactory scopeFactory) : IScheduleHandleStore
{
    /// <inheritdoc />
    public async Task SaveAsync(ScheduleHandle handle, CancellationToken ct = default)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

            // Idempotent upsert: re-saving the same ScheduleId (e.g. a re-scheduled topic getting a new
            // broker sequence number, or a retry after a partial failure) updates the existing row
            // instead of throwing a primary-key violation.
            var existing = await db.ScheduleHandles
                .FirstOrDefaultAsync(e => e.ScheduleId == handle.ScheduleId, ct).ConfigureAwait(false);

            if (existing is null)
            {
                db.ScheduleHandles.Add(new ScheduleHandleRecord
                {
                    ScheduleId = handle.ScheduleId,
                    Topic = handle.Topic,
                    SequenceNumber = handle.SequenceNumber,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.Topic = handle.Topic;
                existing.SequenceNumber = handle.SequenceNumber;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ScheduleHandle?> GetAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
            var record = await db.ScheduleHandles.AsNoTracking()
                .FirstOrDefaultAsync(e => e.ScheduleId == scheduleId, ct).ConfigureAwait(false);
            return record is null ? null : new ScheduleHandle(record.ScheduleId, record.Topic, record.SequenceNumber);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
            await db.ScheduleHandles
                .Where(e => e.ScheduleId == scheduleId)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
    }
}
