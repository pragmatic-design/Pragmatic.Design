using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Storage.InMemory;

/// <summary>
///     Extension methods to register <see cref="InMemoryFileStorage"/>.
/// </summary>
public static class InMemoryStorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers a single <see cref="InMemoryFileStorage"/> instance as both the
        ///     <see cref="IFileStorage"/> and the <see cref="IFileInfoProvider"/> singleton.
        /// </summary>
        /// <remarks>
        ///     Both interfaces resolve to the <em>same</em> instance, so metadata queries via
        ///     <see cref="IFileInfoProvider"/> see the files written through <see cref="IFileStorage"/>.
        ///     Intended for tests and local development — nothing is persisted.
        /// </remarks>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// // In a test host:
        /// var provider = new ServiceCollection()
        ///     .AddInMemoryStorage()
        ///     .BuildServiceProvider();
        ///
        /// var storage = provider.GetRequiredService&lt;IFileStorage&gt;();
        /// var uri = await storage.SaveAsync(content, "avatar.png", "avatars");
        ///
        /// // Same instance answers metadata queries:
        /// var info = provider.GetRequiredService&lt;IFileInfoProvider&gt;();
        /// var meta = await info.GetInfoAsync(uri);
        ///     </code>
        /// </example>
        public IServiceCollection AddInMemoryStorage()
        {
            services.AddSingleton<InMemoryFileStorage>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<InMemoryFileStorage>());
            services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<InMemoryFileStorage>());

            return services;
        }
    }
}
