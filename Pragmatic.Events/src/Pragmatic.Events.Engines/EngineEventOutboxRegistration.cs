using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;

namespace Pragmatic.Events.Engines;

/// <summary>
///     What the registration of a boundary's outbox does on either engine (<see cref="Sql.SqlEventOutboxExtensions" />,
///     <see cref="Documents.DocEventOutboxExtensions" />): the services every outbox shares, and the loop that runs the boundary's drainer.
/// </summary>
internal static class EngineEventOutboxRegistration
{
    /// <summary>The options and the allowlist every outbox shares; false when <typeparamref name="TBoundary" />'s outbox is registered already.</summary>
    public static bool Shared<TBoundary>(IServiceCollection services, Action<EventOutboxOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(IEngineEventOutboxDrainer<TBoundary>)))
            return false;
        var options = new EventOutboxOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        // The allowlist of the event types a row may name: the handlers registered, read when it is first resolved (fail-closed: an event no
        // handler takes is not deserialized).
        services.TryAddSingleton<IEventOutboxTypeResolver>(_ => EventOutboxTypeResolver.FromHandlers(services));
        return true;
    }

    /// <summary>The background loop of <typeparamref name="TBoundary" />'s drainer.</summary>
    public static IServiceCollection Loop<TBoundary>(IServiceCollection services)
    {
        services.AddSingleton<IHostedService>(static sp => new EngineEventOutboxDeliveryService<TBoundary>(sp.GetRequiredService<IEngineEventOutboxDrainer<TBoundary>>(),
            sp.GetRequiredService<EventOutboxOptions>(), sp.GetRequiredService<ILogger<EngineEventOutboxDeliveryService<TBoundary>>>()));
        return services;
    }
}
