using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Polls every registered <see cref="ISagaTimeoutRunner"/> on a fixed cadence,
///     giving each saga type a chance to compensate / terminate instances whose
///     <c>TimeoutAt</c> has elapsed. Registered once per app by
///     <c>AddPragmaticSagas()</c> when at least one saga in the assembly uses
///     <c>[SagaTimeout]</c>.
/// </summary>
public sealed partial class SagaTimeoutBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<SagaTimeoutOptions> options,
    ILogger<SagaTimeoutBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _pollInterval = options.Value.PollInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarted(_pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A misbehaving runner shouldn't kill the loop — next iteration tries again.
                LogScanFailed(ex);
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        LogServiceStopped();
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var runners = scope.ServiceProvider.GetServices<ISagaTimeoutRunner>();
        var asOf = DateTimeOffset.UtcNow;

        foreach (var runner in runners)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await runner.RunDueTimeoutsAsync(asOf, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRunnerFailed(runner.GetType().Name, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Saga timeout background service started (poll interval: {PollInterval})")]
    partial void LogServiceStarted(TimeSpan pollInterval);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saga timeout background service stopped")]
    partial void LogServiceStopped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Saga timeout scan failed")]
    partial void LogScanFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saga timeout runner {RunnerType} threw")]
    partial void LogRunnerFailed(string runnerType, Exception ex);
}
