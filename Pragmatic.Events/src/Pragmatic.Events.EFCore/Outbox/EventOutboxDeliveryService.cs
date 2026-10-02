using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     Background service that runs <see cref="IEventOutboxDrainer{TContext}.DrainOnceAsync" /> on a
///     timer, so events written to the outbox are delivered without anybody asking.
/// </summary>
/// <remarks>
///     <para>
///         The loop and nothing else: what a pass does is
///         <see cref="EventOutboxDrainer{TContext}" />, which an application can also call directly.
///         Two callers, one implementation — a second copy of the delivery logic for tests is how the
///         tested one and the running one stop being the same.
///     </para>
///     <para>
///         Delivery is <b>at-least-once</b>; see the drainer for what that means for a handler.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The application DbContext that owns the outbox table.</typeparam>
public sealed class EventOutboxDeliveryService<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly IEventOutboxDrainer<TContext> _drainer;
    private readonly EventOutboxOptions _options;
    private readonly ILogger<EventOutboxDeliveryService<TContext>> _logger;

    /// <summary>Creates the delivery service.</summary>
    public EventOutboxDeliveryService(
        IEventOutboxDrainer<TContext> drainer,
        EventOutboxOptions options,
        ILogger<EventOutboxDeliveryService<TContext>> logger)
    {
        _drainer = drainer;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _drainer.DrainOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event outbox delivery cycle failed");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
