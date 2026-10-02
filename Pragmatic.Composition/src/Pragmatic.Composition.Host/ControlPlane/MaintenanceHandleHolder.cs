namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Singleton that owns the active maintenance handle shared between
///     <see cref="MaintenanceCommandHandler"/> and <see cref="ExitMaintenanceCommandHandler"/>.
///     All access is protected by a lock to prevent TOCTOU races.
/// </summary>
public sealed class MaintenanceHandleHolder
{
    private readonly Lock _lock = new();
    private IDisposable? _activeHandle;

    /// <summary>
    ///     Stores the active maintenance handle.
    ///     Returns false if one is already active (idempotent enter guard).
    /// </summary>
    public bool TrySet(IDisposable handle)
    {
        lock (_lock)
        {
            if (_activeHandle is not null)
                return false;

            _activeHandle = handle;
            return true;
        }
    }

    /// <summary>
    ///     Clears and returns the active handle, or null if none was set.
    /// </summary>
    public IDisposable? TakeHandle()
    {
        lock (_lock)
        {
            var h = _activeHandle;
            _activeHandle = null;
            return h;
        }
    }

    /// <summary>
    ///     Returns true when a handle is currently active.
    /// </summary>
    public bool HasActiveHandle
    {
        get { lock (_lock) return _activeHandle is not null; }
    }
}
