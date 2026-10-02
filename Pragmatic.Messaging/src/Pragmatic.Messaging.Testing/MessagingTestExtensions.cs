using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Messaging.Testing;

/// <summary>
///     DI extensions for registering <see cref="MessageBusTestHarness"/> in test projects.
/// </summary>
public static class MessagingTestExtensions
{
    /// <summary>
    ///     Replaces the <see cref="IMessageBus"/> registration with a RECORD-ONLY
    ///     <see cref="MessageBusTestHarness"/> (singleton): publish/send are recorded, nothing
    ///     is dispatched. Returns the harness instance for assertions.
    /// </summary>
    public static MessageBusTestHarness AddMessagingTestHarness(this IServiceCollection services)
    {
        var harness = new MessageBusTestHarness();
        services.RemoveAll<IMessageBus>();
        services.AddSingleton<IMessageBus>(harness);
        services.AddSingleton(harness);
        return harness;
    }

    /// <summary>
    ///     Replaces the <see cref="IMessageBus"/> registration with a DISPATCHING
    ///     <see cref="MessageBusTestHarness"/>: publish/send are recorded AND delivered to the
    ///     registered <see cref="IMessageHandler{T}"/> implementations, with per-handler
    ///     <see cref="MessageBusTestHarness.Consumed"/>/<see cref="MessageBusTestHarness.Faulted"/>
    ///     tracking (handler failures are recorded, not rethrown). Resolve the harness from the
    ///     built provider: <c>sp.GetRequiredService&lt;MessageBusTestHarness&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddDispatchingTestHarness(this IServiceCollection services)
    {
        services.RemoveAll<IMessageBus>();
        services.RemoveAll<MessageBusTestHarness>();
        services.AddSingleton(sp => new MessageBusTestHarness(sp));
        services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<MessageBusTestHarness>());
        return services;
    }
}
