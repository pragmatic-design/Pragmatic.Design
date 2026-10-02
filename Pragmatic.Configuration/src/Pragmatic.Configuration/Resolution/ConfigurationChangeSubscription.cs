namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     A registered typed-handler subscription: the section it watches plus a closed-over invoker that
///     dispatches a change to every <c>IConfigurationChangeHandler&lt;TOptions&gt;</c> for that option. The
///     generic type is captured statically at registration time, so the dispatcher needs no reflection
///     (no <c>MakeGenericType</c>) to fan out.
/// </summary>
/// <param name="Section">The configuration section this option binds to (e.g. "Booking").</param>
/// <param name="Invoke">Resolves the option's handlers from the scope and awaits each one.</param>
internal sealed record ConfigurationChangeSubscription(
    string Section,
    Func<IServiceProvider, ConfigurationChange, CancellationToken, Task> Invoke);
