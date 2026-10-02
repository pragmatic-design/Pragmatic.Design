using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage;

/// <summary>
///     Extension methods to register <see cref="IFileStorage"/> implementations.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers a single <see cref="LocalDiskFileStorage"/> instance as both the
        ///     <see cref="IFileStorage"/> and the <see cref="IFileInfoProvider"/> singleton.
        /// </summary>
        /// <remarks>
        ///     Both interfaces resolve to the <em>same</em> instance, so metadata queries see the files
        ///     written through the storage — and an application that injects
        ///     <see cref="IFileInfoProvider"/> builds here exactly as it does under
        ///     <c>AddInMemoryStorage</c>. Registering <see cref="IFileStorage"/> alone would let such an
        ///     application resolve under its tests and fail to start in production.
        /// </remarks>
        /// <param name="basePath">
        ///     Root directory for file storage (e.g. <c>IHostEnvironment.WebRootPath</c>).
        ///     A <c>files/</c> sub-directory will be created automatically.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddLocalDiskStorage(string basePath)
        {
            services.AddSingleton(sp =>
                new LocalDiskFileStorage(
                    basePath,
                    sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));

            return ForwardCapabilities(services);
        }

        /// <summary>
        ///     Registers a single <see cref="LocalDiskFileStorage"/> instance as both the
        ///     <see cref="IFileStorage"/> and the <see cref="IFileInfoProvider"/> singleton, with a
        ///     maximum accepted upload size.
        /// </summary>
        /// <param name="basePath">
        ///     Root directory for file storage (e.g. <c>IHostEnvironment.WebRootPath</c>).
        ///     A <c>files/</c> sub-directory will be created automatically.
        /// </param>
        /// <param name="maxFileSizeBytes">
        ///     Maximum accepted size of a single uploaded file, in bytes. <c>0</c> means no limit.
        ///     Set a positive value to reject oversized uploads before they fill the disk.
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddLocalDiskStorage(string basePath, long maxFileSizeBytes)
        {
            services.AddSingleton(sp =>
                new LocalDiskFileStorage(
                    basePath,
                    sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
                    maxFileSizeBytes));

            return ForwardCapabilities(services);
        }

        /// <summary>
        ///     Registers a custom <see cref="IFileStorage"/> implementation as singleton.
        /// </summary>
        /// <remarks>
        ///     ⚠️ <see cref="IFileStorage"/> only. A custom provider that also implements
        ///     <see cref="IFileInfoProvider"/> or <see cref="ISignedUrlProvider"/> has to register those
        ///     itself — register the concrete type and forward each capability to it, the way the
        ///     shipped <c>AddLocalDiskStorage</c> and <c>AddInMemoryStorage</c> do. Registering the
        ///     capability with its own factory would build a second instance.
        /// </remarks>
        /// <typeparam name="TStorage">The storage implementation type.</typeparam>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddFileStorage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStorage>()
            where TStorage : class, IFileStorage
        {
            services.AddSingleton<IFileStorage, TStorage>();
            return services;
        }
    }

    /// <summary>
    ///     Points <see cref="IFileStorage"/> and <see cref="IFileInfoProvider"/> at the one
    ///     <see cref="LocalDiskFileStorage"/> registered above.
    /// </summary>
    /// <remarks>
    ///     Forwarding rather than a second factory: two factories would build two instances, and a
    ///     provider that holds a connection, a client or a cache would then be two of those while
    ///     looking like one storage.
    /// </remarks>
    private static IServiceCollection ForwardCapabilities(IServiceCollection services)
    {
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalDiskFileStorage>());
        services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<LocalDiskFileStorage>());

        return services;
    }
}
