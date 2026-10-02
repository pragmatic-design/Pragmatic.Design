using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Extension methods for registering Result.AspNetCore services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Adds Pragmatic.Result.AspNetCore services to the service collection.
        /// </summary>
        /// <returns>The service collection for chaining</returns>
        /// <remarks>
        ///     <para>
        ///         This method registers the default services:
        ///         <list type="bullet">
        ///             <item><see cref="IProblemDetailsFactory" /> - Factory for creating ProblemDetails from errors</item>
        ///             <item><see cref="IErrorMessageResolver" /> - Default (null) resolver for error messages</item>
        ///         </list>
        ///     </para>
        ///     <para>
        ///         For automatic Result handling, also configure:
        ///         <list type="bullet">
        ///             <item>Minimal APIs: Use <c>app.MapGroup("").WithResultHandling()</c></item>
        ///             <item>Controllers: Use <c>options.Filters.Add&lt;ResultActionFilter&gt;()</c></item>
        ///         </list>
        ///     </para>
        /// </remarks>
        /// <example>
        ///     <code>
        /// // Register services
        /// builder.Services.AddPragmaticResult();
        /// 
        /// // For Controllers - add global filter
        /// builder.Services.AddControllers(options =>
        /// {
        ///     options.Filters.Add&lt;ResultActionFilter&gt;();
        /// });
        /// 
        /// var app = builder.Build();
        /// 
        /// // For Minimal APIs - add filter to root group
        /// app.MapGroup("").WithResultHandling()
        ///    .MapGet("/users/{id}", async (int id, UserService svc)
        ///        => await svc.GetUserAsync(id));
        /// </code>
        /// </example>
        public IServiceCollection AddPragmaticResult()
        {
            ArgumentNullException.ThrowIfNull(services);

            // Register default (null) resolver - can be overridden
            services.TryAddSingleton<IErrorMessageResolver>(NullErrorMessageResolver.Instance);

            // Register the problem details factory
            services.TryAddSingleton<IProblemDetailsFactory, DefaultProblemDetailsFactory>();

            return services;
        }

        /// <summary>
        ///     Adds Pragmatic.Result.AspNetCore services with a custom error message resolver.
        /// </summary>
        /// <typeparam name="TResolver">The resolver implementation type</typeparam>
        /// <returns>The service collection for chaining</returns>
        /// <remarks>
        ///     <para>
        ///         Use this overload to register a custom <see cref="IErrorMessageResolver" /> for localization.
        ///         The resolver is used to provide localized "detail" messages in ProblemDetails responses.
        ///     </para>
        ///     <para>
        ///         The custom resolver <b>replaces</b> any previously registered
        ///         <see cref="IErrorMessageResolver" /> (including the default <c>NullErrorMessageResolver</c>),
        ///         so the outcome is deterministic regardless of call order relative to
        ///         <c>AddPragmaticResult()</c>.
        ///     </para>
        /// </remarks>
        /// <example>
        ///     <code>
        /// // Register with custom localizer
        /// builder.Services.AddPragmaticResult&lt;ResourceErrorMessageResolver&gt;();
        /// 
        /// public class ResourceErrorMessageResolver : IErrorMessageResolver
        /// {
        ///     private readonly IStringLocalizer _localizer;
        /// 
        ///     public ResourceErrorMessageResolver(IStringLocalizer localizer)
        ///         => _localizer = localizer;
        /// 
        ///     public string? Resolve(string code, object? context)
        ///         => _localizer[code]?.Value;
        /// }
        /// </code>
        /// </example>
        public IServiceCollection AddPragmaticResult<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
            where TResolver : class, IErrorMessageResolver
        {
            ArgumentNullException.ThrowIfNull(services);

            // The custom resolver replaces any prior registration (default or otherwise) so the
            // effective resolver is deterministic and the custom one always wins over NullErrorMessageResolver.
            services.RemoveAll<IErrorMessageResolver>();
            services.AddSingleton<IErrorMessageResolver, TResolver>();

            // Register the problem details factory
            services.TryAddSingleton<IProblemDetailsFactory, DefaultProblemDetailsFactory>();

            return services;
        }
    }
}