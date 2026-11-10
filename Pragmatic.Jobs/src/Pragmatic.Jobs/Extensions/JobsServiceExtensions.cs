using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Serialization;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Extensions;

/// <summary>
///     DI registration for Pragmatic.Jobs.
/// </summary>
public static class JobsServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic.Jobs services to the service collection.
        /// </summary>
        public IServiceCollection AddPragmaticJobs(Action<JobsBuilder>? configure = null)
        {
            var builder = new JobsBuilder(services);
            configure?.Invoke(builder);

            var options = builder.Options;

            // Register options as singleton
            services.AddSingleton(options);

            // Default stores. Skipped entirely when UseEfCore() asked for durable persistence, so
            // the in-memory fallback can never silently win over a store registered later.
            if (!options.UseEfCore)
            {
                services.TryAddSingleton<IJobStore, InMemoryJobStore>();
                services.TryAddSingleton<IRecurringJobStore, InMemoryRecurringJobStore>();
            }

            // The one registry the runtime consumes, over every assembly's generated one.
            //
            // ⚠️ A composite and not a single registry: the generator emits one per assembly, and a
            // registration that Replaces whatever is there makes two modules declaring jobs cancel each
            // other out, and a job type shipped by a package never run at all. With none
            // registered it refuses every job and says that nothing was registered.
            services.TryAddSingleton<IJobTypeRegistry, CompositeJobTypeRegistry>();

            // Default clock (SystemClock)
            services.TryAddSingleton<IClock>(SystemClock.Instance);

            // Shared JSON serialization seam (AOT-configurable via UseJson)
            services.AddPragmaticJson();

            // Scheduler
            services.TryAddScoped<IJobScheduler, JobScheduler>();

            // Seeds NextExecutionAt for declared recurring definitions. Scoped so it composes with
            // both the singleton in-memory store and the scoped EF Core store.
            services.TryAddScoped<IRecurringJobRegistrar, RecurringJobRegistrar>();

            // Background services — call AddJobProcessingServices() to register when ready
            // In Composition mode, the SG-generated host template handles this.
            // For standalone: services.AddJobProcessingServices();

            return services;
        }

        /// <summary>
        ///     Registers the background processing services (JobProcessorService + RecurringJobSchedulerService).
        ///     Call this after all job registrations are complete (SG-generated or manual).
        /// </summary>
        /// <remarks>
        ///     Registers nothing in a host configured with <see cref="JobsBuilder.EnqueueOnly" />: the configuration that
        ///     applies is the last one <c>AddPragmaticJobs</c> registered, as for every other option.
        /// </remarks>
        public IServiceCollection AddJobProcessingServices()
        {
            if (EffectiveOptions(services) is { EnqueueOnly: true })
                return services;

            services.AddHostedService<JobProcessorService>();
            services.AddHostedService<RecurringJobSchedulerService>();
            return services;
        }
    }

    /// <summary>Takes out the processor and the recurring scheduler, for a host that only enqueues.</summary>
    internal static void RemoveJobProcessingServices(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                && (descriptor.ImplementationType == typeof(JobProcessorService)
                    || descriptor.ImplementationType == typeof(RecurringJobSchedulerService)))
            {
                services.RemoveAt(i);
            }
        }
    }

    /// <summary>The options the provider will resolve: the last instance registered.</summary>
    private static JobsOptions? EffectiveOptions(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(JobsOptions) && services[i].ImplementationInstance is JobsOptions options)
                return options;
        }

        return null;
    }
}
