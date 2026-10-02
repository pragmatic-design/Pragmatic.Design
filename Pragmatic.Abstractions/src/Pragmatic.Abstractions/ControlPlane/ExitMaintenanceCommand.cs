namespace Pragmatic.ControlPlane;

/// <summary>
///     Instructs a host to exit maintenance mode and return to ready state.
/// </summary>
public sealed record ExitMaintenanceCommand : HostCommand
{
    /// <inheritdoc />
    public override string CommandTypeName => nameof(ExitMaintenanceCommand);
}
