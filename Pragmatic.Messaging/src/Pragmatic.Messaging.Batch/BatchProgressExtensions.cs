using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Wiring for EF-backed batch progress. The <c>[EnableBatchProgress]</c> boundary attribute drives the
///     source generator to map the <c>__BatchProgress</c> table into that boundary's DbContext
///     (<c>OnModelCreating</c>) and to emit <see cref="AddBatchProgress{TContext}"/> in the host DI
///     registration.
/// </summary>
public static class BatchProgressExtensions
{
    /// <summary>
    ///     Registers the single-owner EF <see cref="IBatchProgressStore"/> against the boundary DbContext
    ///     <typeparamref name="TContext"/> that hosts the <c>__BatchProgress</c> table, replacing the default
    ///     in-memory store. The generator emits this exactly once — batch progress is a single store, so the
    ///     single-owner constraint is enforced at compile time (PRAG0834).
    /// </summary>
    /// <typeparam name="TContext">The generated boundary DbContext that owns the batch-progress table.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddBatchProgress<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        // Single store — replace any prior registration (e.g. the in-memory default from EnableBatchProcessing).
        services.RemoveAll<IBatchProgressStore>();
        services.AddScoped<IBatchProgressStore>(sp => new EfCoreBatchProgressStore(
            sp.GetRequiredService<TContext>(),
            sp.GetRequiredService<ILogger<EfCoreBatchProgressStore>>(),
            sp.GetService<ITenantContext>()));
        return services;
    }
}
