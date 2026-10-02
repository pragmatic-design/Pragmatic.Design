using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Receives serialized commands from the control plane and dispatches to
///     the appropriate <see cref="IHostCommandHandler{TCommand}"/> registered in DI.
/// </summary>
public sealed class HostCommandDispatcher(
    IServiceProvider services,
    ILogger<HostCommandDispatcher> logger) : IHostCommandDispatcher
{
    // commandType name → (deserialize func, dispatch func)
    /// <remarks>
    ///     Declared before the table that reads it: a static field initialised later is null while the
    ///     table is being built, and only the fact that these are lambdas — read at dispatch time, not
    ///     at initialisation — kept that from being a NullReferenceException.
    ///     <para>
    ///         The resolver comes from the shared seam, so control-plane commands resolve through the
    ///         generated contexts instead of by reflection.
    ///     </para>
    /// </remarks>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = global::Pragmatic.Serialization.PragmaticJsonOptions.Default.Build().TypeInfoResolver,
    };

    private static readonly FrozenDictionary<string, CommandRegistration> _commands =
        new Dictionary<string, CommandRegistration>
        {
            // All commands share the same case-insensitive options — a sibling using bare
            // Deserialize<T>(json) would silently fail property binding when the control plane sends
            // a different casing than the command's declared property names.
            [nameof(EnterMaintenanceCommand)] = new(
                json => JsonSerializer.Deserialize(json, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<EnterMaintenanceCommand>(_jsonOptions))!,
                (sp, cmd, ct) => Resolve<EnterMaintenanceCommand>(sp, cmd, ct)),

            [nameof(ExitMaintenanceCommand)] = new(
                json => JsonSerializer.Deserialize(json, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<ExitMaintenanceCommand>(_jsonOptions))!,
                (sp, cmd, ct) => Resolve<ExitMaintenanceCommand>(sp, cmd, ct)),

            [nameof(DrainCommand)] = new(
                json => JsonSerializer.Deserialize(json, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<DrainCommand>(_jsonOptions))!,
                (sp, cmd, ct) => Resolve<DrainCommand>(sp, cmd, ct)),

            [nameof(MigrateCommand)] = new(
                json => JsonSerializer.Deserialize(json, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<MigrateCommand>(_jsonOptions))!,
                (sp, cmd, ct) => Resolve<MigrateCommand>(sp, cmd, ct)),
        }.ToFrozenDictionary();

    /// <summary>
    ///     Dispatches a command received from the control plane hub.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Exceptions are logged and swallowed BY DESIGN. This method is invoked from the
    ///         Agent-delivered command path (the daemon pushes a Command frame over the socket);
    ///         letting an exception propagate would break the receive loop and stop the host from
    ///         receiving further commands. A single malformed or failing command must not take the
    ///         dispatcher offline.
    ///     </para>
    ///     <para>
    ///         Command outcome is observable via the structured logs emitted here
    ///         (<c>Command {CommandType} executed successfully</c> / <c>Command {CommandType} failed</c>)
    ///         and via each handler's own effect on host status. The
    ///         <see cref="IHostCommandDispatcher.DispatchAsync"/> contract is intentionally
    ///         fire-and-forget (<see cref="Task"/>, not a result); a result-returning overload would
    ///         be a separate, opt-in API and is tracked for a future control-plane iteration.
    ///     </para>
    /// </remarks>
    public async Task DispatchAsync(string commandType, string commandJson, CancellationToken ct = default)
    {
        if (!_commands.TryGetValue(commandType, out var registration))
        {
            logger.LogWarning("Unknown command type received: {CommandType}", commandType);
            return;
        }

        try
        {
            var command = registration.Deserialize(commandJson);
            logger.LogInformation("Dispatching command: {CommandType}", commandType);
            await registration.Dispatch(services, command, ct).ConfigureAwait(false);
            logger.LogInformation("Command {CommandType} executed successfully", commandType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command {CommandType} failed", commandType);
        }
    }

    private static async Task Resolve<TCommand>(IServiceProvider sp, HostCommand cmd, CancellationToken ct)
        where TCommand : HostCommand
    {
        var handler = sp.GetService<IHostCommandHandler<TCommand>>();
        if (handler is null)
        {
            var logger = sp.GetRequiredService<ILogger<HostCommandDispatcher>>();
            logger.LogWarning("No handler registered for command {CommandType}", typeof(TCommand).Name);
            return;
        }

        await handler.HandleAsync((TCommand)cmd, ct).ConfigureAwait(false);
    }

    private sealed record CommandRegistration(
        Func<string, HostCommand> Deserialize,
        Func<IServiceProvider, HostCommand, CancellationToken, Task> Dispatch);
}
