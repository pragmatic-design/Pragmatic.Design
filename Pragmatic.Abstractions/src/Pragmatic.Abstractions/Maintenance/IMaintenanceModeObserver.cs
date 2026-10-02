namespace Pragmatic.Maintenance;

/// <summary>
///     Observer notified when maintenance mode is activated or deactivated.
///     Used by the control plane to broadcast maintenance state changes.
/// </summary>
public interface IMaintenanceModeObserver
{
    /// <summary>Called when maintenance mode is activated.</summary>
    Task OnActivatedAsync(string reason, TimeSpan? eta);

    /// <summary>Called when maintenance mode is deactivated.</summary>
    Task OnDeactivatedAsync();
}
