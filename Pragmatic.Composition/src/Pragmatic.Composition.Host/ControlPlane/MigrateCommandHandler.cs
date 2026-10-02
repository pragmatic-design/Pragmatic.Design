using Microsoft.Extensions.Logging;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.ControlPlane;

/// <summary>
///     Handles <see cref="MigrateCommand"/> — an operational "migrate now" trigger. Delegates to the
///     SG-generated <see cref="IHostMigrationCoordinator"/>, whose Migrations runner is DB-lock-gated, so
///     a node that does not hold the lock safely waits / no-ops. Replaces the former silent "no handler
///     registered" no-op.
/// </summary>
public sealed class MigrateCommandHandler(
    ILogger<MigrateCommandHandler> logger,
    IHostMigrationCoordinator? coordinator = null) : IHostCommandHandler<MigrateCommand>
{
    public async Task HandleAsync(MigrateCommand command, CancellationToken ct = default)
    {
        if (coordinator is null)
        {
            logger.LogWarning(
                "MigrateCommand received but no host migration coordinator is available "
                + "(Pragmatic.Migrations not referenced, or the host declares no databases). "
                + "Database schema is managed at host startup; ignoring the command.");
            return;
        }

        logger.LogInformation(
            "MigrateCommand received{Filter} — running host migrations (DB-lock-gated).",
            command.DatabaseFilter is { } f ? $" (filter: {f})" : string.Empty);

        await coordinator.MigrateAllAsync(ct).ConfigureAwait(false);

        logger.LogInformation("MigrateCommand completed.");
    }
}
