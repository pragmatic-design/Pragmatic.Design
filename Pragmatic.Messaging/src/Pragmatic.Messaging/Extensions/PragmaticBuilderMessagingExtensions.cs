using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.Extensions;

/// <summary>
///     Extension methods for configuring messaging on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderMessagingExtensions
{
    /// <summary>
    ///     Configures the Pragmatic messaging infrastructure.
    /// </summary>
    /// <example>
    /// <code>
    /// await PragmaticApp.RunAsync(args, app =>
    /// {
    ///     app.UseMessaging(msg =>
    ///     {
    ///         msg.UseInMemory();
    ///         msg.EnableOutbox();
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IPragmaticBuilder UseMessaging(
        this IPragmaticBuilder builder,
        Action<MessagingBuilder>? configure = null)
    {
        builder.Services.AddPragmaticMessaging(msg => configure?.Invoke(msg));

        // The outbox delivery pump and purge service are registered per-boundary by the SG
        // (MessagingOutboxExtensions.AddMessagingOutbox in the generated DbContext registration)
        // whenever a [Boundary] carries [EnableOutbox] and the app references Pragmatic.Messaging.EFCore.
        // EnableOutbox() only tunes MessagingOptions (polling/batch/retention) — it wires no global
        // pump, which would drain nothing without the per-boundary IOutboxSource.
        return builder;
    }
}
