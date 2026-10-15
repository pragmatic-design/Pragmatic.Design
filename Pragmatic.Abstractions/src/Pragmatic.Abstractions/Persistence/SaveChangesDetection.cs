using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence;

/// <summary>
///     Which save has already detected changes, shared by the EF Core interceptors of every assembly that
///     takes part in one, so that one <c>SaveChanges</c> runs one change-detection pass before EF's own.
/// </summary>
/// <remarks>
///     <para>
///         <c>ChangeTracker.Entries()</c> detects changes over every tracked entity on each call, and every
///         framework interceptor enumerates the tracker: a save ran a full pass per interceptor, ten in a host
///         with tenancy, events, auditing and an outbox (#137). Now the first interceptor of a save detects and
///         claims it, and the others enumerate what it found.
///     </para>
///     <para>
///         The save is the <c>DbContextEventData</c> instance EF Core hands every interceptor of one
///         <c>SaveChanges</c> call. It is typed <see cref="object" /> because this assembly has no EF Core:
///         Persistence.EFCore, Events.EFCore and Messaging all reach it, and no EF Core assembly in common.
///     </para>
///     <para>
///         ⚠️ Skipping detection is sound only because an interceptor that changes an entity it found
///         <c>Unchanged</c> detects that entity itself (<c>EntityEntry.DetectChanges()</c>), as the roll-up
///         does for the parents it adjusts. Writes to entities already Added or Modified need nothing: their
///         state does not change, and EF's own pass inside the save picks up the new values.
///     </para>
/// </remarks>
public static class SaveChangesDetection
{
    private static readonly object Marker = new();

    // Weak: a save or a context that is gone takes its entry with it.
    private static readonly ConditionalWeakTable<object, object> ClaimedSaves = new();
    private static readonly ConditionalWeakTable<object, object> DetectedContexts = new();

    /// <summary>
    ///     Claims <paramref name="save" /> for the caller.
    /// </summary>
    /// <param name="save">The event data of the save in progress.</param>
    /// <param name="context">The context being saved.</param>
    /// <returns>
    ///     <see langword="true" /> when the caller is the first of this save and the context was not detected
    ///     just before it (<see cref="Detected" />): the caller must detect changes. <see langword="false" />
    ///     for everyone else.
    /// </returns>
    public static bool Claim(object save, object context)
        => ClaimedSaves.TryAdd(save, Marker) && !DetectedContexts.Remove(context);

    /// <summary>
    ///     Records that the caller has just detected changes on <paramref name="context" /> and is about to
    ///     save it, so the first interceptor of that save does not detect again.
    /// </summary>
    /// <returns>A scope to dispose once the save returns, whether it ran an interceptor or not.</returns>
    /// <remarks>
    ///     Only for a caller that saves straight after detecting, with no change in between that detection
    ///     would have to see: the unit of work that fills generated values, a context that checks its entries
    ///     before the save.
    /// </remarks>
    public static IDisposable Detected(object context)
    {
        DetectedContexts.AddOrUpdate(context, Marker);
        return new DetectedScope(context);
    }

    private sealed class DetectedScope(object context) : IDisposable
    {
        public void Dispose() => DetectedContexts.Remove(context);
    }
}
