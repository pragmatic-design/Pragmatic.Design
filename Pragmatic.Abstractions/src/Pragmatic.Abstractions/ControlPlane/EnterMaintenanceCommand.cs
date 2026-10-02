namespace Pragmatic.ControlPlane;

/// <summary>
///     Instructs a host to enter maintenance mode.
/// </summary>
/// <param name="Reason">Human-readable reason for maintenance.</param>
/// <param name="Eta">Estimated duration. Null if unknown.</param>
public sealed record EnterMaintenanceCommand(string Reason, TimeSpan? Eta = null) : HostCommand
{
    /// <inheritdoc />
    public override string CommandTypeName => nameof(EnterMaintenanceCommand);
}
