using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Messaging.Extensions;

/// <summary>
///     Registration for transport message subscriptions. Each call adds one <see cref="MessageSubscription"/>
///     marker; the SG-generated <c>AddPragmaticMessageHandlers</c> calls this once per message type so the
///     active transport's consumer service can bind subscriptions without reflection.
/// </summary>
public static class MessagingSubscriptionExtensions
{
    /// <summary>Registers a transport subscription for <typeparamref name="TMessage"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="subscriber">
    ///     Who is listening, as the broker should know it — this module's own name. Required: it is what
    ///     keeps two services consuming one event from sharing a queue.
    /// </param>
    public static IServiceCollection AddMessageSubscription<TMessage>(
        this IServiceCollection services, string subscriber)
        where TMessage : notnull
    {
        services.AddSingleton(new MessageSubscription(typeof(TMessage), subscriber));
        return services;
    }

    /// <summary>
    ///     Registers a transport subscription for <typeparamref name="TMessage"/> on a NAMED bus:
    ///     the named bus's consumer service binds it on its own transport ([OnBus] handlers).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="busName">The named bus.</param>
    /// <param name="subscriber">See the other method.</param>
    /// <remarks>
    ///     ⚠️ A method of its own rather than a second string on the one above: as an overload,
    ///     <c>AddMessageSubscription&lt;T&gt;(services, "analytics")</c> would bind the bus name to
    ///     <c>subscriber</c> and compile — a silent change of meaning, in generated code nobody reads
    ///     by hand.
    /// </remarks>
    public static IServiceCollection AddMessageSubscriptionOnBus<TMessage>(
        this IServiceCollection services, string busName, string subscriber)
        where TMessage : notnull
    {
        services.AddSingleton(new MessageSubscription(typeof(TMessage), subscriber, busName));
        return services;
    }
}
