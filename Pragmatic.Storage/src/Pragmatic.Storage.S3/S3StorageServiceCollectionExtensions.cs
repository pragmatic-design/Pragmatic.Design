using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.S3;

/// <summary>
///     Extension methods to register <see cref="S3FileStorage" /> as the
///     <see cref="IFileStorage" /> implementation.
/// </summary>
public static class S3StorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers <see cref="S3FileStorage" /> as the <see cref="IFileStorage" /> singleton.
        ///     The <see cref="IAmazonS3" /> client is resolved from the container: register and
        ///     configure it yourself (credentials, region, or an S3-compatible endpoint such as
        ///     Cloudflare R2 / MinIO).
        /// </summary>
        /// <param name="options">S3 storage options (bucket, key prefix, public base URL, max file size).</param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddSingleton&lt;IAmazonS3&gt;(new AmazonS3Client(RegionEndpoint.EUWest1));
        /// services.AddS3Storage(new S3StorageOptions
        /// {
        ///     BucketName = "myapp-files",
        ///     MaxFileSizeBytes = 10 * 1024 * 1024,
        /// });
        ///     </code>
        /// </example>
        public IServiceCollection AddS3Storage(S3StorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            services.TryAddSingleton(options);

            // One instance, forwarded to every capability it implements. A second factory per
            // interface would build a second S3FileStorage with its own client and its own bucket
            // configuration, and the two would answer about different objects while looking like one
            // storage.
            services.AddSingleton(sp => new S3FileStorage(
                sp.GetRequiredService<IAmazonS3>(),
                options,
                sp.GetRequiredService<ILogger<S3FileStorage>>()));

            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
            services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<S3FileStorage>());
            services.AddSingleton<ISignedUrlProvider>(sp => sp.GetRequiredService<S3FileStorage>());

            return services;
        }
    }
}
