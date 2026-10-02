using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Singleton runtime maintenance mode service.
///     Thread-safe via volatile bool for fast-path check and Lock for state mutation.
///     Notifies <see cref="IMaintenanceModeObserver"/> instances on state changes.
/// </summary>
public sealed partial class MaintenanceModeService : IMaintenanceMode
{
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates the service. <paramref name="logger"/> is optional so the parameterless
    ///     <c>new MaintenanceModeService()</c> still works in tests and manual wiring; the DI
    ///     container supplies a real logger. When null, a <see cref="NullLogger"/> is used.
    /// </summary>
    public MaintenanceModeService(ILogger<MaintenanceModeService>? logger = null)
        => _logger = logger ?? NullLogger<MaintenanceModeService>.Instance;

    private readonly Lock _lock = new();
    private readonly List<IMaintenanceModeObserver> _observers = [];
    private volatile bool _isActive;
    // Number of live maintenance handles. Maintenance is active while this is > 0; a single
    // Deactivate (handle dispose) must NOT turn maintenance off while other handles are still held —
    // otherwise two concurrent Enter commands could leave the host serving traffic while the holder
    // still reports an active handle (state desync during a deploy).
    private int _activeCount;
    private string? _reason;
    private DateTimeOffset? _activatedAt;
    private DateTimeOffset? _estimatedEnd;

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <inheritdoc />
    public string? Reason
    {
        get
        {
            lock (_lock) return _reason;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? ActivatedAt
    {
        get
        {
            lock (_lock) return _activatedAt;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? EstimatedEnd
    {
        get
        {
            lock (_lock) return _estimatedEnd;
        }
    }

    /// <summary>
    ///     Registers an observer that will be notified on maintenance mode changes.
    ///     Thread-safe: acquires lock to prevent concurrent modification during notifications.
    /// </summary>
    public void AddObserver(IMaintenanceModeObserver observer)
    {
        lock (_lock) _observers.Add(observer);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Observers are notified fire-and-forget outside the lock so this synchronous call does not block
    ///     on observer I/O. Dispose the returned handle to deactivate maintenance mode.
    /// </remarks>
    public IDisposable Activate(string reason, TimeSpan? eta = null)
    {
        bool firstActivation;
        lock (_lock)
        {
            _reason = reason;
            _activatedAt ??= DateTimeOffset.UtcNow; // keep the original start across nested activations
            _estimatedEnd = eta.HasValue ? DateTimeOffset.UtcNow.Add(eta.Value) : _estimatedEnd;
            firstActivation = _activeCount == 0;
            _activeCount++;
            _isActive = true;
        }

        // Notify observers only on the inactive→active edge (fire-and-forget, outside the lock).
        // Activate() is a synchronous API that returns a handle and must not block on observer I/O.
        // The discard is safe because NotifyActivatedAsync catches AND logs every per-observer
        // exception internally, so the discarded Task cannot surface an unobserved fault.
        if (firstActivation)
            _ = NotifyActivatedAsync(reason, eta);

        return new MaintenanceHandle(this);
    }

    private void Deactivate()
    {
        bool lastDeactivation;
        lock (_lock)
        {
            if (_activeCount == 0)
                return; // already inactive (defensive — MaintenanceHandle disposes at most once)

            _activeCount--;
            lastDeactivation = _activeCount == 0;
            if (lastDeactivation)
            {
                _isActive = false;
                _reason = null;
                _activatedAt = null;
                _estimatedEnd = null;
            }
        }

        // Only notify (and only turn maintenance off) on the active→inactive edge — while other
        // handles are still held, disposing one is a no-op for observers and for IsActive.
        if (lastDeactivation)
            _ = NotifyDeactivatedAsync();
    }

    private async Task NotifyActivatedAsync(string reason, TimeSpan? eta)
    {
        // Take a snapshot under lock so AddObserver cannot mutate the list during async iteration
        IMaintenanceModeObserver[] snapshot;
        lock (_lock) { snapshot = [.. _observers]; }

        foreach (var observer in snapshot)
        {
            try { await observer.OnActivatedAsync(reason, eta).ConfigureAwait(false); }
            catch (Exception ex) { LogObserverFailed(ex, observer.GetType().Name, "activated"); }
        }
    }

    private async Task NotifyDeactivatedAsync()
    {
        IMaintenanceModeObserver[] snapshot;
        lock (_lock) { snapshot = [.. _observers]; }

        foreach (var observer in snapshot)
        {
            try { await observer.OnDeactivatedAsync().ConfigureAwait(false); }
            catch (Exception ex) { LogObserverFailed(ex, observer.GetType().Name, "deactivated"); }
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Maintenance observer {Observer} threw on {Transition} notification; ignored so it cannot break maintenance mode")]
    private partial void LogObserverFailed(Exception ex, string observer, string transition);

    private sealed class MaintenanceHandle(MaintenanceModeService service) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                service.Deactivate();
        }
    }
}
