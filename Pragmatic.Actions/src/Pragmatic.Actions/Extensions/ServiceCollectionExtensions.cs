using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Configuration;
using Pragmatic.Actions.Pipeline;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.Extensions;

/// <summary>
///     Extension methods for registering Pragmatic.Actions services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds the core Pragmatic.Actions services to the service collection.
        ///     This includes the action pipeline and is required for all action execution.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        ///     If no ILoggerFactory is registered, a NullLoggerFactory is used.
        ///     For production, ensure Microsoft.Extensions.Logging is configured.
        /// </remarks>
        public IServiceCollection AddPragmaticActions()
        {
            return services.AddPragmaticActions(_ => { });
        }

        /// <summary>
        ///     Adds the core Pragmatic.Actions services to the service collection with options.
        /// </summary>
        /// <param name="configure">Action to configure options.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddPragmaticActions(Action<PragmaticActionsOptions> configure)
        {
            ThrowIfNull(services);
            ThrowIfNull(configure);

            var options = new PragmaticActionsOptions();
            configure(options);

            // Fallback to NullLoggerFactory if no logging is configured
            services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));

            // Action call context for internal call detection
            // Register both concrete type and interface — ICallContext enables infrastructure
            // components (e.g., InMemoryEventDispatcher) to enter internal mode without
            // depending on Pragmatic.Actions.
            services.TryAddScoped<Pipeline.ActionCallContext>();
            services.TryAddScoped<Pragmatic.Pipeline.ICallContext>(sp =>
                sp.GetRequiredService<Pipeline.ActionCallContext>());

            // Built-in filters (opt-in/out via options).
            // TryAddEnumerable prevents duplicate registration when AddPragmaticActions() is called multiple times.
            if (options.EnableValidationFilter)
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IActionFilter, Pipeline.Filters.ValidationFilter>());

            if (options.EnableLoggingFilter)
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IActionFilter, Pipeline.Filters.LoggingFilter>());

            if (options.EnablePermissionFilter)
            {
                // Fail closed when the generator produced no registry: an assembly with actions
                // always gets one, empty if nothing is declared, so an absent registry means the
                // generator did not run and nobody can say what this action requires.
                services.TryAddSingleton<IPermissionRequirementRegistry>(UnavailablePermissionRequirementRegistry.Instance);
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IActionFilter, PermissionAuthorizationFilter>());
            }

            if (options.EnableResourceAuthorizationFilter)
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IActionFilter, ResourceAuthorizationFilter>());

            if (options.EnablePolicyFilter)
            {
                // Fail closed — see the note on the permission registry above.
                services.TryAddSingleton<IPolicyRegistry>(UnavailablePolicyRegistry.Instance);
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IActionFilter, PolicyEvaluationFilter>());
            }

            return services;
        }

        /// <summary>
        ///     Adds a global action filter that applies to all actions.
        /// </summary>
        /// <typeparam name="TFilter">The filter type.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddActionFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter>()
            where TFilter : class, IActionFilter
        {
            services.AddScoped<IActionFilter, TFilter>();
            return services;
        }

        /// <summary>
        ///     Adds an action-specific filter that applies to a specific action type regardless of return type.
        ///     Works with both <see cref="DomainAction{TReturn}" /> and <see cref="VoidDomainAction" />.
        /// </summary>
        /// <typeparam name="TFilter">The filter type.</typeparam>
        /// <typeparam name="TAction">The action type this filter applies to.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddActionFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter, TAction>()
            where TFilter : class, IActionFilter<TAction>
        {
            services.AddScoped<IActionFilter<TAction>, TFilter>();
            return services;
        }

        /// <summary>
        ///     Adds an action-specific filter that applies only to a specific action type with a known return type.
        /// </summary>
        /// <typeparam name="TFilter">The filter type.</typeparam>
        /// <typeparam name="TAction">The action type this filter applies to.</typeparam>
        /// <typeparam name="TReturn">The return type of the action.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddActionFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter, TAction, TReturn>()
            where TFilter : class, IActionFilter<TAction, TReturn>
            where TAction : DomainAction<TReturn>
        {
            services.AddScoped<IActionFilter<TAction, TReturn>, TFilter>();
            return services;
        }
    }
}
