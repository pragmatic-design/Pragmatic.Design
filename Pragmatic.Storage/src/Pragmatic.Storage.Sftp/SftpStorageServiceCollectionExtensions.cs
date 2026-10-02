using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.Sftp;

/// <summary>
///     Extension methods to register <see cref="SftpFileStorage" /> as the
///     <see cref="IFileStorage" /> implementation.
/// </summary>
public static class SftpStorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers <see cref="SftpFileStorage" /> as the <see cref="IFileStorage" /> (and
        ///     <see cref="IFileInfoProvider" />) singleton. Connections are opened per operation from
        ///     the supplied <paramref name="options" />.
        /// </summary>
        /// <param name="options">
        ///     SFTP options (host, port, credentials, remote base path, max file size).
        /// </param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddSftpStorage(new SftpStorageOptions
        /// {
        ///     Host = "sftp.example.com",
        ///     Username = "app",
        ///     PrivateKeyPath = "/secrets/id_rsa",
        ///     BasePath = "/uploads",
        ///     MaxFileSizeBytes = 10 * 1024 * 1024,
        /// });
        ///     </code>
        /// </example>
        public IServiceCollection AddSftpStorage(SftpStorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            services.TryAddSingleton(options);

            // One instance, forwarded to every capability it implements. A second factory per
            // interface would build a second SftpFileStorage opening its own connections to the same
            // host, and the two would look like one storage while holding two sessions.
            services.AddSingleton(sp => new SftpFileStorage(
                options,
                sp.GetRequiredService<ILogger<SftpFileStorage>>()));

            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<SftpFileStorage>());
            services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<SftpFileStorage>());

            return services;
        }
    }
}
