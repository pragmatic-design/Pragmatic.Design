using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Messaging.Dashboard;

/// <summary>
///     Maps the messaging dashboard endpoints. Order 85: after infrastructure (routing) and
///     before module steps, mirroring the maintenance admin surface.
/// </summary>
public sealed class MessagingDashboardStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 85;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        if (app is WebApplication webApp)
        {
            var options = app.ApplicationServices.GetService<MessagingDashboardOptions>()
                          ?? new MessagingDashboardOptions();

            MessagingDashboardEndpoints.Map(webApp, options);
        }
    }
}
