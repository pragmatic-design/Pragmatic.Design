namespace Casework.Intake.Infrastructure.Jobs;

/// <summary>
///     Stops the cases whose verification deadline has passed from waiting.
/// </summary>
/// <remarks>
///     The unit the tests drive, on purpose: the job above it only decides <em>when</em> this runs, and a
///     test that went through the scheduler would be asserting the polling interval and the lease as well
///     as the expiry. That the schedule itself is registered and durable is
///     asserted separately, by reading its row.
/// </remarks>
public interface IExpireOverdueVerifications
{
    /// <summary>
    ///     Expires every case still awaiting an answer that was due before <paramref name="now" />, and
    ///     answers how many.
    /// </summary>
    /// <param name="now">
    ///     The instant to compare against — the application's clock, passed in by the job, never read
    ///     here. It is what lets a test seal the clock instead of waiting ten days.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> RunAsync(DateTimeOffset now, CancellationToken ct = default);
}
