using Pragmatic.Result;

namespace Pragmatic.ControlPlane;

/// <summary>
///     Error returned by control plane operations.
/// </summary>
/// <param name="Code">Stable machine-readable error code (e.g. <c>CP_HOST_NOT_FOUND</c>).</param>
/// <param name="Message">Human-readable description of the failure.</param>
public sealed record ControlPlaneError(string Code, string Message) : IError
{
    /// <inheritdoc />
    string IError.Title => Message;

    /// <inheritdoc />
    int IError.StatusCode => 502;

    /// <inheritdoc />
    string? IError.Description => null;

    /// <summary>
    ///     Creates an error indicating the target host is not connected to the control plane.
    /// </summary>
    /// <param name="hostId">Identifier of the host that could not be reached.</param>
    /// <returns>A <see cref="ControlPlaneError"/> with code <c>CP_HOST_NOT_FOUND</c>.</returns>
    public static ControlPlaneError HostNotFound(string hostId)
        => new("CP_HOST_NOT_FOUND", $"Host '{hostId}' is not connected to the control plane.");

    /// <summary>
    ///     Creates an error indicating the caller is not connected to the control plane.
    /// </summary>
    /// <returns>A <see cref="ControlPlaneError"/> with code <c>CP_NOT_CONNECTED</c>.</returns>
    public static ControlPlaneError NotConnected()
        => new("CP_NOT_CONNECTED", "Not connected to the control plane.");

    /// <summary>
    ///     Creates an error indicating a dispatched command failed.
    /// </summary>
    /// <param name="reason">Description of why the command failed.</param>
    /// <returns>A <see cref="ControlPlaneError"/> with code <c>CP_COMMAND_FAILED</c>.</returns>
    public static ControlPlaneError CommandFailed(string reason)
        => new("CP_COMMAND_FAILED", reason);
}
