using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     The tracked entries of the save in progress, read by an interceptor: the first one of the save
///     detects changes, the others read what it found (<see cref="SaveChangesDetection" />).
/// </summary>
/// <remarks>
///     Events.EFCore and Messaging have the same few lines, because none of the three can reach the others;
///     what they must agree on, the claim, is the one in <see cref="SaveChangesDetection" />.
/// </remarks>
internal static class SaveEntries
{
    public static IEnumerable<EntityEntry> All(DbContextEventData save)
    {
        var tracker = Detect(save);
        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            return tracker.Entries();
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    public static IEnumerable<EntityEntry<T>> Of<T>(DbContextEventData save) where T : class
    {
        var tracker = Detect(save);
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

    // A context with auto-detect off asked for no detection, and gets none: EF skips its own pass too.
    private static ChangeTracker Detect(DbContextEventData save)
    {
        var context = save.Context!;
        var tracker = context.ChangeTracker;
        if (tracker.AutoDetectChangesEnabled && SaveChangesDetection.Claim(save, context))
            tracker.DetectChanges();
        return tracker;
    }
}
