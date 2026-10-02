namespace Pragmatic.ControlPlane;

/// <summary>
///     Mutable runtime status of a Pragmatic host.
///     Updated by the host lifecycle (startup, migrations, maintenance mode, shutdown)
///     and reported to the control plane via heartbeat.
/// </summary>
public interface IHostStatus
{
    /// <summary>
    ///     Current lifecycle state.
    /// </summary>
    HostState State { get; }

    /// <summary>
    ///     Human-readable reason for the current state (e.g. "Database initialization").
    ///     Null when in <see cref="HostState.Ready"/>.
    /// </summary>
    string? StateReason { get; }

    /// <summary>
    ///     UTC timestamp of the last state transition.
    /// </summary>
    DateTimeOffset StateChangedAt { get; }

    /// <summary>
    ///     Migration progress when <see cref="State"/> is <see cref="HostState.Migrating"/>.
    ///     Null otherwise.
    /// </summary>
    MigrationStatus? MigrationStatus { get; }

    /// <summary>
    ///     Transitions the host to a new state.
    ///     <para>
    ///         <b>Thread safety:</b> implementations must guard this method against concurrent
    ///         callers (e.g. via a lock or interlocked operation). The interface itself provides
    ///         no concurrency guarantee; unguarded concurrent transitions from multiple threads
    ///         risk data races on <see cref="State"/> and <see cref="StateChangedAt"/>.
    ///     </para>
    /// </summary>
    void TransitionTo(HostState newState, string? reason = null);

    /// <summary>
    ///     Updates migration progress while in <see cref="HostState.Migrating"/> state.
    ///     This method is only semantically valid when <see cref="State"/> equals
    ///     <see cref="HostState.Migrating"/>; calling it in any other state will set
    ///     <see cref="MigrationStatus"/> in a semantically inconsistent context.
    ///     <para>
    ///         <b>Thread safety:</b> see <see cref="TransitionTo"/> — same concurrency
    ///         constraints apply.
    ///     </para>
    /// </summary>
    void UpdateMigrationProgress(MigrationStatus status);
}
