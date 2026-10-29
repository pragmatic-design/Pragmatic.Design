using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;

namespace Pragmatic.Events.Engines;

/// <summary>
///     The background loop of a boundary's event outbox on an engine: <see cref="IEngineEventOutboxDrainer{TBoundary}.DrainOnceAsync" /> every
///     <see cref="EventOutboxOptions.PollingInterval" />, so the events written are delivered without anybody asking. The loop and nothing else:
///     a pass is the drainer's, which an application can call too.
/// </summary>
/// <typeparam name="TBoundary">The boundary whose outbox is delivered.</typeparam>
public sealed partial class EngineEventOutboxDeliveryService<TBoundary>(IEngineEventOutboxDrainer<TBoundary> drainer, EventOutboxOptions options,
    ILogger<EngineEventOutboxDeliveryService<TBoundary>> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await drainer.DrainOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                LogCycleFailed(logger, failure); // a pass that fails (the database away) is tried again at the next interval
            }

            try
            {
                await Task.Delay(options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event outbox delivery cycle failed")]
    private static partial void LogCycleFailed(ILogger logger, Exception failure);
}
