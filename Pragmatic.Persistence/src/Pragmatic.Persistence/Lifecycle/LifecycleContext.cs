namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Context passed to entity lifecycle hooks (OnCreating, OnSaving, CreatePresets, GetEvents).
///     Provides access to clock, current user, and other contextual information.
/// </summary>
public sealed record LifecycleContext
{
    /// <summary>
    ///     The current UTC time. Use this instead of <c>DateTimeOffset.UtcNow</c> for testability.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>required</c>, and it is the whole point: a default of <c>DateTimeOffset.UtcNow</c> —
    ///     the very thing the line above says to use it instead of — would make any construction that
    ///     forgot it silently read the wall clock. It could not fail, it could not be pinned, and it
    ///     would look right at the call site because the property is named for the value it should
    ///     carry. Omitting it is a compile error.
    /// </remarks>
    public required DateTimeOffset Now { get; init; }

    /// <summary>A context at a given instant.</summary>
    /// <param name="instant">The instant the hooks should treat as now.</param>
    /// <remarks>
    ///     The shape for a caller that has an instant, and the one that absorbs the noise
    ///     <c>required</c> would otherwise put on every construction. ⚠️ It reads nothing: two calls
    ///     with the same instant produce equal contexts, which is what a parameterless default
    ///     reintroducing the clock could not do.
    /// </remarks>
    public static LifecycleContext At(DateTimeOffset instant) => new() { Now = instant };

    /// <summary>A context at the instant a provider reports.</summary>
    /// <param name="clock">The provider to read once.</param>
    /// <remarks>
    ///     For a caller holding a <see cref="TimeProvider" /> — a pinned one in a test, the system one
    ///     in production — so the unwrapping is written here instead of at every call site.
    /// </remarks>
    public static LifecycleContext At(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return At(clock.GetUtcNow());
    }

    /// <summary>
    ///     The current user ID, if available.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    ///     The current tenant ID, if available.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Additional context data for custom lifecycle hooks.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
