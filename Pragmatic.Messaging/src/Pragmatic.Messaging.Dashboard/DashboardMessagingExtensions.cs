using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.Dashboard;

/// <summary>
///     Extension methods enabling the operational dashboard on <see cref="MessagingBuilder"/>.
/// </summary>
public static class DashboardMessagingExtensions
{
    /// <summary>
    ///     Enables the messaging ops API + HTML panel under
    ///     <see cref="MessagingDashboardOptions.Path"/> (default <c>/_messaging</c>):
    ///     status, outbox, dead-letters with replay/delete, active sagas, audit trail.
    ///     Protect with <see cref="MessagingDashboardOptions.ApiKey"/> (X-Messaging-Key header);
    ///     without a key the endpoints answer localhost only. Excluded from OpenAPI.
    /// </summary>
    public static MessagingBuilder EnableDashboard(
        this MessagingBuilder builder,
        Action<MessagingDashboardOptions>? configure = null)
    {
        var options = new MessagingDashboardOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton(options);
        builder.Services.AddSingleton<IStartupStep, MessagingDashboardStep>();
        return builder;
    }
}
