using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Persistence;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     The tracked entries of the save in progress, read by an interceptor: the first one of the save
///     detects changes, the others read what it found (<see cref="SaveChangesDetection" />).
/// </summary>
/// <remarks>
///     Persistence.EFCore and Events.EFCore have the same few lines, because none of the three can reach
///     the others; what they must agree on, the claim, is the one in <see cref="SaveChangesDetection" />.
/// </remarks>
internal static class SaveEntries
{
    public static IEnumerable<EntityEntry<T>> Of<T>(DbContextEventData save) where T : class
    {
        var context = save.Context!;
        var tracker = context.ChangeTracker;
        // A context with auto-detect off asked for no detection, and gets none: EF skips its own pass too.
        if (tracker.AutoDetectChangesEnabled && SaveChangesDetection.Claim(save, context))
            tracker.DetectChanges();

        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            return tracker.Entries<T>();
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }
}
