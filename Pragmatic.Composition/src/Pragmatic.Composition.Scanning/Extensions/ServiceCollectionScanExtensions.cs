using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Scanning;

namespace Pragmatic.Composition.Extensions;

/// <summary>
///     The entry point to convention-based registration.
/// </summary>
/// <remarks>
///     Kept in its own package because it is the one part of composition that cannot be
///     source-generated: the fluent API takes runtime predicates (<c>Where(Func&lt;Type, bool&gt;)</c>,
///     <c>FromAssembliesMatching(string)</c>), which a compile-time pass cannot evaluate. Registering by
///     attribute — which the generator does see — is the AOT-safe equivalent and needs nothing from here.
/// </remarks>
public static class ServiceCollectionScanExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Scans assemblies for types matching the configured criteria and registers them.
        /// </summary>
        public IServiceCollection Scan(Action<IAssemblyScanner> configure)
        {
            return services.Scan(configure, null);
        }

        /// <summary>
        ///     Scans assemblies for types matching the configured criteria and registers them,
        ///     with an optional error callback for logging scan failures.
        /// </summary>
        public IServiceCollection Scan(Action<IAssemblyScanner> configure,
            Action<string, Exception?>? onScanError)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var scanner = new AssemblyScanner(services, onScanError);
            configure(scanner);
            return services;
        }
    }
}
