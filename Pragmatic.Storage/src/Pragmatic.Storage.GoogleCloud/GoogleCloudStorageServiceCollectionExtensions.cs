using Google.Cloud.Storage.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.GoogleCloud;

/// <summary>
///     Extension methods to register <see cref="GoogleCloudFileStorage" /> as the
///     <see cref="IFileStorage" /> implementation.
/// </summary>
public static class GoogleCloudStorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers <see cref="GoogleCloudFileStorage" /> as the <see cref="IFileStorage" />
        ///     singleton. The <see cref="StorageClient" /> is resolved from the container: register
        ///     and configure it yourself (typically via <c>StorageClient.Create()</c>, which
        ///     picks up application-default credentials).
        /// </summary>
        /// <param name="options">
        ///     Google Cloud Storage options (bucket, object prefix, public base URL, max file size,
        ///     optional URL signer).
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddSingleton(StorageClient.Create());
        /// services.AddGoogleCloudStorage(new GoogleCloudStorageOptions
        /// {
        ///     BucketName = "myapp-files",
        ///     MaxFileSizeBytes = 10 * 1024 * 1024,
        /// });
        ///     </code>
        /// </example>
        public IServiceCollection AddGoogleCloudStorage(GoogleCloudStorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            services.TryAddSingleton(options);

            // One instance, forwarded to every capability it implements. A second factory per
            // interface would build a second GoogleCloudFileStorage with its own client and its own
            // bucket, and the two would answer about different objects while looking like one storage.
            services.AddSingleton(sp => new GoogleCloudFileStorage(
                sp.GetRequiredService<StorageClient>(),
                options,
                sp.GetRequiredService<ILogger<GoogleCloudFileStorage>>()));

            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<GoogleCloudFileStorage>());
            services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<GoogleCloudFileStorage>());
            services.AddSingleton<ISignedUrlProvider>(sp => sp.GetRequiredService<GoogleCloudFileStorage>());

            return services;
        }
    }
}
