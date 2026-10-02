namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Tunables for <see cref="SagaTimeoutBackgroundService"/>. Set via
///     <c>services.Configure&lt;SagaTimeoutOptions&gt;(...)</c> before
///     <c>AddPragmaticSagas()</c> if non-default polling is required.
/// </summary>
public sealed class SagaTimeoutOptions
{
    private TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often the background service scans for expired timeouts.
    ///     Defaults to 5 seconds — tight enough for interactive demos,
    ///     conservative enough to not hammer the store in production.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when set to a value &lt;= <see cref="TimeSpan.Zero"/>;
    ///     a non-positive interval would spin the background service in a tight loop.
    /// </exception>
    public TimeSpan PollInterval
    {
        get => _pollInterval;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "PollInterval must be greater than TimeSpan.Zero to avoid a tight background-poll loop.");
            _pollInterval = value;
        }
    }
}
