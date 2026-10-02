using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     In-memory, thread-safe host status tracker.
///     Uses volatile bool for fast-path reads and Lock for state mutations.
/// </summary>
public sealed class LocalHostStatus : IHostStatus
{
    private readonly Lock _lock = new();
    private HostState _state = HostState.Starting;
    private string? _stateReason;
    private DateTimeOffset _stateChangedAt = DateTimeOffset.UtcNow;
    private MigrationStatus? _migrationStatus;

    /// <inheritdoc />
    /// <remarks>
    ///     Lock-protected so callers that read both <see cref="State"/> and
    ///     <see cref="StateReason"/> in sequence always observe a consistent snapshot.
    /// </remarks>
    public HostState State
    {
        get { lock (_lock) return _state; }
    }

    /// <inheritdoc />
    public string? StateReason
    {
        get { lock (_lock) return _stateReason; }
    }

    /// <inheritdoc />
    public DateTimeOffset StateChangedAt
    {
        get { lock (_lock) return _stateChangedAt; }
    }

    /// <inheritdoc />
    public MigrationStatus? MigrationStatus
    {
        get { lock (_lock) return _migrationStatus; }
    }

    /// <inheritdoc />
    public void TransitionTo(HostState newState, string? reason = null)
    {
        lock (_lock)
        {
            _state = newState;
            _stateReason = reason;
            _stateChangedAt = DateTimeOffset.UtcNow;

            // Clear migration status when leaving Migrating state
            if (newState != HostState.Migrating)
                _migrationStatus = null;
        }
    }

    /// <inheritdoc />
    public void UpdateMigrationProgress(MigrationStatus status)
    {
        lock (_lock)
        {
            // Only accept progress while actually migrating. TransitionTo clears _migrationStatus on
            // leaving Migrating; without this guard a late/out-of-order progress callback could
            // resurrect stale migration data in (say) Ready or Stopped state. Ignore silently — a
            // tardy callback is not an error worth throwing for.
            if (_state != HostState.Migrating)
                return;

            _migrationStatus = status;
        }
    }
}
