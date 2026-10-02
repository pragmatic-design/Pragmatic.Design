using System.Text.Json.Serialization;

namespace Pragmatic.ControlPlane;

/// <summary>
///     Base for commands sent to a specific host via the control plane.
///     Polymorphic serialization is driven by the <c>$type</c> discriminator;
///     no runtime reflection is required in application code.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DrainCommand), nameof(DrainCommand))]
[JsonDerivedType(typeof(EnterMaintenanceCommand), nameof(EnterMaintenanceCommand))]
[JsonDerivedType(typeof(ExitMaintenanceCommand), nameof(ExitMaintenanceCommand))]
[JsonDerivedType(typeof(MigrateCommand), nameof(MigrateCommand))]
public abstract record HostCommand
{
    /// <summary>
    ///     Discriminator name used for wire serialization.
    ///     Each concrete command overrides this with a compile-time constant via <c>nameof</c>.
    /// </summary>
    public abstract string CommandTypeName { get; }

    /// <summary>
    ///     Unique identity of this command instance. Used for idempotency: a handler may
    ///     deduplicate by <see cref="CommandId"/> so a redelivered command is applied once.
    ///     Defaults to a fresh GUID string per instance.
    /// </summary>
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Optional correlation identifier for tracing a command across hosts and logs.
    ///     <c>null</c> when not part of a correlated flow.
    /// </summary>
    public string? CorrelationId { get; init; }
}
