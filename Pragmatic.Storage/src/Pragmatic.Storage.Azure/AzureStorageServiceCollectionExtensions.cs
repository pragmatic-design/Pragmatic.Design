using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.Azure;

/// <summary>
///     Extension methods to register <see cref="AzureBlobFileStorage" /> as the
///     <see cref="IFileStorage" /> implementation.
/// </summary>
public static class AzureStorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers <see cref="AzureBlobFileStorage" /> as the <see cref="IFileStorage" /> singleton.
        ///     The <see cref="BlobServiceClient" /> is resolved from the container: register it yourself
        ///     (e.g. via <c>AddAzureClients</c>) or use the connection-string overload.
        /// </summary>
        /// <param name="options">Azure Blob storage options (container prefix, max file size).</param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddSingleton(new BlobServiceClient(connectionString));
        /// services.AddAzureBlobStorage(new AzureBlobStorageOptions
        /// {
        ///     ContainerPrefix = "myapp-",
        ///     MaxFileSizeBytes = 10 * 1024 * 1024,
        /// });
        ///     </code>
        /// </example>
        public IServiceCollection AddAzureBlobStorage(AzureBlobStorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            services.TryAddSingleton(options);

            // One instance, forwarded to every capability it implements. A second factory per
            // interface would build a second AzureBlobFileStorage with its own client and its own
            // container cache, and the two would look like one storage while talking to different
            // accounts.
            services.AddSingleton(sp => new AzureBlobFileStorage(
                sp.GetRequiredService<BlobServiceClient>(),
                options,
                sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));

            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<AzureBlobFileStorage>());
            services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<AzureBlobFileStorage>());
            services.AddSingleton<ISignedUrlProvider>(sp => sp.GetRequiredService<AzureBlobFileStorage>());

            return services;
        }

        /// <summary>
        ///     Registers a <see cref="BlobServiceClient" /> for <paramref name="connectionString" />
        ///     and <see cref="AzureBlobFileStorage" /> as the <see cref="IFileStorage" /> singleton.
        ///     An already-registered <see cref="BlobServiceClient" /> takes precedence.
        /// </summary>
        /// <param name="options">Azure Blob storage options (container prefix, max file size).</param>
        /// <param name="connectionString">Azure Storage connection string.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddAzureBlobStorage(
        ///     new AzureBlobStorageOptions { MaxFileSizeBytes = 10 * 1024 * 1024 },
        ///     builder.Configuration.GetConnectionString("BlobStorage")!);
        ///     </code>
        /// </example>
        public IServiceCollection AddAzureBlobStorage(AzureBlobStorageOptions options, string connectionString)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            services.TryAddSingleton(_ => new BlobServiceClient(connectionString));
            return services.AddAzureBlobStorage(options);
        }
    }
}
