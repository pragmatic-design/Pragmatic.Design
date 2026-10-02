using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.Ftp;

/// <summary>
///     Extension methods to register <see cref="FtpFileStorage" /> as the
///     <see cref="IFileStorage" /> implementation.
/// </summary>
public static class FtpStorageServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers <see cref="FtpFileStorage" /> as the <see cref="IFileStorage" /> singleton,
        ///     connecting to the FTP / FTPS server described by <paramref name="options" />.
        /// </summary>
        /// <param name="options">FTP storage options (host, credentials, base path, TLS, max file size).</param>
        /// <returns>The service collection for chaining.</returns>
        /// <example>
        ///     <code>
        /// services.AddFtpStorage(new FtpStorageOptions
        /// {
        ///     Host = "ftp.example.com",
        ///     Username = "uploads",
        ///     Password = "secret",
        ///     UseSsl = true,
        ///     BasePath = "/data",
        ///     MaxFileSizeBytes = 10 * 1024 * 1024,
        /// });
        ///     </code>
        /// </example>
        public IServiceCollection AddFtpStorage(FtpStorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            services.TryAddSingleton(options);

            // One instance, forwarded to every capability it implements. A second factory per
            // interface would build a second FtpFileStorage opening its own connections to the same
            // host, and the two would look like one storage while holding two sessions.
            services.AddSingleton(sp => new FtpFileStorage(
                options,
                sp.GetRequiredService<ILogger<FtpFileStorage>>()));

            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<FtpFileStorage>());
            services.AddSingleton<IFileInfoProvider>(sp => sp.GetRequiredService<FtpFileStorage>());

            return services;
        }
    }
}
