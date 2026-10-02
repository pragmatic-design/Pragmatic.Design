using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.EFCore;

/// <summary>
///     Extension methods for enabling EF Core-backed persistence in Pragmatic.Messaging.
/// </summary>
public static class MessagingEfCoreExtensions
{
    /// <summary>
    ///     Replaces in-memory stores with EF Core-backed implementations.
    ///     Registers <see cref="EfCoreIdempotencyStore"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Requires a <c>MessagingDbContext</c> (or your own DbContext with
    ///     <see cref="MessagingDbContext.ApplyMessagingConfigurations"/> applied)
    ///     to be registered in DI.
    ///     </para>
    ///     <para>
    ///     The outbox source (<see cref="EfCoreOutboxSource"/>) is registered per-boundary
    ///     by the SG-generated code, not by this extension.
    ///     </para>
    /// </remarks>
    public static MessagingBuilder EnableEfCorePersistence(this MessagingBuilder builder)
    {
        // Replace in-memory idempotency store with EF Core-backed
        builder.Services.RemoveAll<IIdempotencyStore>();
        builder.Services.AddScoped<IIdempotencyStore, EfCoreIdempotencyStore>();

        // Replace in-memory audit store with EF Core-backed

        // Durable schedule handles: broker-native schedulers (ASB) become restart-safe
        // for cancellation. Singleton — the scheduler that consumes it is a singleton.
        builder.Services.TryAddSingleton<IScheduleHandleStore, EfCoreScheduleHandleStore>();

        return builder;
    }
}
