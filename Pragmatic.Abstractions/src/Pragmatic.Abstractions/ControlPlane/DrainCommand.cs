namespace Pragmatic.ControlPlane;

/// <summary>
///     Instructs a host to drain active requests and prepare for shutdown.
/// </summary>
/// <param name="GracePeriod">How long to wait for in-flight requests before forced shutdown.</param>
public sealed record DrainCommand(TimeSpan GracePeriod) : HostCommand
{
    /// <inheritdoc />
    public override string CommandTypeName => nameof(DrainCommand);
}
