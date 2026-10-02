namespace Pragmatic.Configuration;

/// <summary>
///     Reacts to changes of a specific options type. When any configuration key under
///     <typeparamref name="TOptions" />'s section changes in the store, the framework invokes this handler —
///     the typed, per-option counterpart to the raw <c>IConfigurationStore.WatchAsync</c> stream.
/// </summary>
/// <typeparam name="TOptions">The options type whose section is watched.</typeparam>
public interface IConfigurationChangeHandler<TOptions>
    where TOptions : class
{
    /// <summary>
    ///     Called when a key under <typeparamref name="TOptions" />'s section changed. Re-read the option
    ///     (e.g. via the resolver or <c>IOptionsMonitor</c>) to obtain the new value.
    /// </summary>
    /// <param name="change">The change that triggered this callback (key, new value, tenant).</param>
    /// <param name="ct">Cancellation for host shutdown.</param>
    Task OnChangedAsync(ConfigurationChange change, CancellationToken ct = default);
}
