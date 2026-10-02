namespace Pragmatic.ControlPlane;

/// <summary>
///     Dispatches serialized commands received from the control plane to
///     the appropriate <see cref="IHostCommandHandler{TCommand}"/>.
/// </summary>
public interface IHostCommandDispatcher
{
    /// <summary>
    ///     Dispatches a command by type name and JSON payload.
    ///     <para>
    ///         <b>Security:</b> <paramref name="commandType"/> must be validated against a
    ///         known-type allow-list before deserialization. If <paramref name="commandType"/>
    ///         is caller-controlled (e.g. received from a network message) without validation,
    ///         an attacker could supply an arbitrary type name to trigger unsafe deserialization.
    ///         Implementations must resolve the concrete type from a registered handler registry
    ///         (e.g. the SG-generated <c>IHostTypeRegistry</c>) rather than calling
    ///         <c>Type.GetType(commandType)</c> directly.
    ///     </para>
    /// </summary>
    Task DispatchAsync(string commandType, string commandJson, CancellationToken ct = default);
}
