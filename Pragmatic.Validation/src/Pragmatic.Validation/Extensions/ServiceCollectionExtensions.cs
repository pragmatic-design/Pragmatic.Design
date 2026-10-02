using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pragmatic.Validation.Extensions;

/// <summary>
///     Extension methods for registering Pragmatic.Validation services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic.Validation core services (<see cref="ValidationOptions" />) to the container.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This registers only the <see cref="ValidationOptions" /> configuration. The
        ///         attribute-generated validators are registered by the generated
        ///         <c>AddGeneratedValidators()</c> extension method — invoked automatically by the
        ///         Pragmatic.Composition host, or call it yourself when self-hosting:
        ///         <c>services.AddGeneratedValidators();</c>.
        ///     </para>
        ///     <para>
        ///         Each generated validator uses the lifetime from its <c>[Validator]</c> attribute
        ///         (default: Scoped).
        ///     </para>
        /// </remarks>
        /// <param name="configure">Optional configuration delegate.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// // Basic usage
        /// builder.Services.AddPragmaticValidation();
        /// 
        /// // With options
        /// builder.Services.AddPragmaticValidation(options =>
        /// {
        ///     options.FailFast = true;
        /// });
        /// </code>
        /// </example>
        public IServiceCollection AddPragmaticValidation(Action<ValidationOptions>? configure = null)
        {
            Ensure.Ensure.ThrowIfNull(services);

            // Register options
            if (configure is not null)
                services.Configure(configure);
            else
                services.TryAddSingleton(Options.Create(new ValidationOptions()));

            // Validators are auto-registered by source generator via ValidationRegistry
            RegisterDiscoveredValidators(services);

            return services;
        }

        /// <summary>
        ///     Registers a combined validator (sync + async) manually.
        /// </summary>
        /// <typeparam name="TValidator">The validator type implementing <see cref="IValidator{T}" />.</typeparam>
        /// <typeparam name="T">The type being validated.</typeparam>
        /// <param name="lifetime">The service lifetime (default: Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddValidator<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator, T>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TValidator : class, IValidator<T>
        {
            Ensure.Ensure.ThrowIfNull(services);

            services.Add(new ServiceDescriptor(typeof(IValidator<T>), typeof(TValidator), lifetime));

            return services;
        }

        /// <summary>
        ///     Registers an async-only validator manually.
        /// </summary>
        /// <typeparam name="TValidator">The validator type implementing <see cref="IAsyncValidator{T}" />.</typeparam>
        /// <typeparam name="T">The type being validated.</typeparam>
        /// <param name="lifetime">The service lifetime (default: Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddAsyncValidator<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator, T>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TValidator : class, IAsyncValidator<T>
        {
            Ensure.Ensure.ThrowIfNull(services);

            services.Add(new ServiceDescriptor(typeof(IAsyncValidator<T>), typeof(TValidator), lifetime));

            return services;
        }

        /// <summary>
        ///     Registers an async validator with automatic <see cref="IValidator{T}" /> registration
        ///     via <see cref="CompositeValidator{T}" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This method registers:
        ///         <list type="bullet">
        ///             <item><see cref="IAsyncValidator{T}" /> → TValidator</item>
        ///             <item><see cref="IValidator{T}" /> → <see cref="CompositeValidator{T}" /> (combines sync + async)</item>
        ///         </list>
        ///     </para>
        ///     <para>
        ///         The <see cref="CompositeValidator{T}" /> will call <see cref="ISyncValidator.Validate()" />
        ///         first (if the instance implements <see cref="ISyncValidator" />), then run
        ///         async validators filtered by <see cref="IAsyncValidatorBindings{T}" /> bindings.
        ///     </para>
        /// </remarks>
        /// <typeparam name="TValidator">The async validator type.</typeparam>
        /// <typeparam name="T">The type being validated.</typeparam>
        /// <param name="lifetime">The service lifetime (default: Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddValidatorWithComposite<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator, T>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
            where TValidator : class, IAsyncValidator<T>
        {
            Ensure.Ensure.ThrowIfNull(services);

            // Register the async validator (supports multiple via Add, not TryAdd)
            services.Add(new ServiceDescriptor(typeof(IAsyncValidator<T>), typeof(TValidator), lifetime));

            // Register CompositeValidator as IValidator<T> (idempotent)
            services.TryAdd(new ServiceDescriptor(typeof(IValidator<T>), typeof(CompositeValidator<T>), lifetime));

            return services;
        }

        /// <summary>
        ///     Registers <see cref="IValidator{T}" /> using <see cref="CompositeValidator{T}" /> for sync-only validation.
        /// </summary>
        /// <remarks>
        ///     Use this when the type has sync validation attributes but no async validator.
        ///     The <see cref="CompositeValidator{T}" /> will only call <see cref="ISyncValidator.Validate()" />.
        /// </remarks>
        /// <typeparam name="T">The type being validated.</typeparam>
        /// <param name="lifetime">The service lifetime (default: Scoped).</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddSyncOnlyValidator<T>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            Ensure.Ensure.ThrowIfNull(services);

            // Register CompositeValidator as IValidator<T> (no async validator)
            services.TryAdd(new ServiceDescriptor(typeof(IValidator<T>), typeof(CompositeValidator<T>), lifetime));

            return services;
        }

        /// <summary>
        ///     Registers async validator bindings for change-aware validation filtering.
        /// </summary>
        /// <typeparam name="TBindings">The generated bindings type.</typeparam>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddAsyncValidatorBindings<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBindings, T>()
            where TBindings : class, IAsyncValidatorBindings<T>
        {
            Ensure.Ensure.ThrowIfNull(services);

            services.TryAddSingleton<IAsyncValidatorBindings<T>, TBindings>();

            return services;
        }
    }

    private static void RegisterDiscoveredValidators(IServiceCollection services)
    {
        // Validators are registered by the source generator via generated extension methods
        // (e.g., AddGeneratedValidators()). No runtime discovery needed.
    }
}
