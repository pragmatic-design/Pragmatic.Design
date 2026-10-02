namespace Pragmatic.ControlPlane;

/// <summary>
///     Handles a specific <see cref="HostCommand"/> received from the control plane.
///     Register implementations in DI — the <c>HostCommandDispatcher</c> resolves them by command type.
/// </summary>
/// <typeparam name="TCommand">The concrete command type this handler processes.</typeparam>
public interface IHostCommandHandler<in TCommand> where TCommand : HostCommand
{
    /// <summary>
    ///     Executes the command.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A task that completes when the command has been handled.</returns>
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}
