using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Seals the trail's due segments on a timer, for as long as the host runs. Registered by
///     <see cref="AuditEfCoreExtensions.AddAuditTrail" />.
/// </summary>
/// <remarks>
///     <para>
///         Sealing is what makes the trail verifiable: <see cref="IAuditTrailReader.VerifyAsync" /> checks
///         sealed segments only. Without something calling <see cref="AuditSealingService" />, no segment
///         is ever sealed, and verification checks none and reports every trail intact — altered or not.
///     </para>
///     <para>
///         The interval is the grace period: a segment becomes sealable one grace period after its window
///         closes, and ticking at that pace seals it at most one grace period later. The first tick waits
///         an interval too, so the host's migrations have created the tables before anything reads them.
///     </para>
///     <para>
///         Several instances may each run one. Sealing is deterministic — the same entries under the same
///         predecessor hash to the same values — so two sealers reaching one segment write the same seal.
///     </para>
/// </remarks>
public sealed partial class AuditSealingWorker(
    IServiceScopeFactory scopes,
    IAuditSegmentNaming naming,
    ILogger<AuditSealingWorker> logger) : BackgroundService
{
    // A PeriodicTimer refuses a zero period, and a naming with no grace period would otherwise ask for one.
    private static readonly TimeSpan ShortestInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = naming.GracePeriod > ShortestInterval ? naming.GracePeriod : ShortestInterval;
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await TickAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>One pass: seals whatever is due, in its own scope.</summary>
    /// <returns>The segments sealed; empty when none was due or the pass failed.</returns>
    internal async Task<IReadOnlyList<string>> TickAsync(CancellationToken ct)
    {
        try
        {
            var scope = scopes.CreateAsyncScope();
            await using var _ = scope.ConfigureAwait(false);
            var sealedIds = await scope.ServiceProvider.GetRequiredService<AuditSealingService>()
                .SealDueSegmentsAsync(ct)
                .ConfigureAwait(false);

            if (sealedIds.Count > 0)
                LogSealed(sealedIds.Count, sealedIds[^1]);

            return sealedIds;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The segments stay due and the next tick seals them. Letting this escape would end the host's
            // only sealer, and the trail would go back to verifying nothing without anyone being told.
            LogSealingFailed(ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sealed {Count} audit segment(s), through {LastSegmentId}")]
    private partial void LogSealed(int count, string lastSegmentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sealing the audit trail failed; the due segments are retried on the next tick")]
    private partial void LogSealingFailed(Exception ex);
}
