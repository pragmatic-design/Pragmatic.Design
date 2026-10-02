using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Holidays;

namespace Pragmatic.Temporal.Extensions;

/// <summary>
///     Extension methods for registering Pragmatic.Temporal services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic.Temporal core services to the service collection.
        /// </summary>
        /// <param name="configure">Optional configuration action.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddPragmaticTemporal(Action<TemporalOptions>? configure = null)
        {
            // Single source of truth for TemporalOptions: the bare singleton resolves the same
            // instance IOptions<TemporalOptions> sees, so the configure delegate here and
            // TemporalBuilder's Configure<> calls all land on one object regardless of which
            // registration a consumer (context factory, middleware, accessor) reads.
            services.AddOptions();
            if (configure is not null)
                services.Configure(configure);

            services.TryAddSingleton<TemporalOptions>(sp =>
                sp.GetRequiredService<IOptions<TemporalOptions>>().Value);

            // Register clock
            services.TryAddSingleton<IClock>(SystemClock.Instance);

            // Register TimeProvider backed by IClock for cross-module interop
            // Other modules (Events, Persistence) use TimeProvider as their timestamp contract
            services.TryAddSingleton<TimeProvider>(sp => sp.GetRequiredService<IClock>().GetTimeProvider());

            // Register holiday provider (default: no holidays)
            services.TryAddSingleton<IHolidayProvider>(NoHolidaysProvider.Instance);

            // Register calculator
            services.TryAddSingleton<ITemporalCalculator>(sp =>
            {
                var holidayProvider = sp.GetRequiredService<IHolidayProvider>();
                return new TemporalCalculator(holidayProvider);
            });

            // Register a default TemporalContext factory (can be overridden by ASP.NET Core middleware)
            services.TryAddScoped(sp =>
            {
                var opts = sp.GetRequiredService<TemporalOptions>();
                var clock = sp.GetRequiredService<IClock>();
                return new TemporalContext
                {
                    Clock = clock,
                    ClientTimeZone = opts.DefaultTimeZone,
                    BusinessTimeZone = opts.BusinessTimeZone,
                    NonExistentTimeHandling = opts.NonExistentTimeHandling,
                    AmbiguousTimeHandling = opts.AmbiguousTimeHandling,
                    FirstDayOfWeek = opts.FirstDayOfWeek,
                    DefaultCountryCode = opts.DefaultCountryCode
                };
            });

            return services;
        }

        /// <summary>
        ///     Adds Pragmatic.Temporal services with a specific holiday provider.
        /// </summary>
        public IServiceCollection AddPragmaticTemporal<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THolidayProvider>(Action<TemporalOptions>? configure = null)
            where THolidayProvider : class, IHolidayProvider
        {
            services.AddPragmaticTemporal(configure);
            services.Replace(ServiceDescriptor.Singleton<IHolidayProvider, THolidayProvider>());
            return services;
        }

        /// <summary>
        ///     Adds Pragmatic.Temporal services with a specific holiday provider instance.
        /// </summary>
        public IServiceCollection AddPragmaticTemporal(IHolidayProvider holidayProvider,
            Action<TemporalOptions>? configure = null)
        {
            services.AddPragmaticTemporal(configure);
            services.Replace(ServiceDescriptor.Singleton<IHolidayProvider>(holidayProvider));
            return services;
        }

        /// <summary>
        ///     Replaces the default clock with a custom implementation.
        ///     Also updates the TimeProvider registration for cross-module consistency.
        ///     Useful for testing.
        /// </summary>
        public IServiceCollection UseClock<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClock>()
            where TClock : class, IClock
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock, TClock>();

            // Keep TimeProvider in sync with the new clock
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<IClock>().GetTimeProvider());

            return services;
        }

        /// <summary>
        ///     Replaces the default clock with a specific instance.
        ///     Also updates the TimeProvider registration for cross-module consistency.
        ///     Useful for testing.
        /// </summary>
        public IServiceCollection UseClock(IClock clock)
        {
            services.RemoveAll<IClock>();
            services.AddSingleton(clock);

            // Keep TimeProvider in sync with the new clock
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock.GetTimeProvider());

            return services;
        }

        /// <summary>
        ///     Replaces the default holiday provider.
        /// </summary>
        public IServiceCollection UseHolidayProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THolidayProvider>()
            where THolidayProvider : class, IHolidayProvider
        {
            services.RemoveAll<IHolidayProvider>();
            services.AddSingleton<IHolidayProvider, THolidayProvider>();
            return services;
        }

        /// <summary>
        ///     Replaces the default holiday provider with a specific instance.
        /// </summary>
        public IServiceCollection UseHolidayProvider(IHolidayProvider provider)
        {
            services.RemoveAll<IHolidayProvider>();
            services.AddSingleton(provider);
            return services;
        }
    }
}