using FluentFTP;

namespace Pragmatic.Storage.Ftp;

/// <summary>
///     Default <see cref="IAsyncFtpClientFactory" /> that builds an <see cref="AsyncFtpClient" />
///     from <see cref="FtpStorageOptions" />.
/// </summary>
/// <remarks>
///     When <see cref="FtpStorageOptions.UseSsl" /> is set, the client is configured for explicit
///     FTPS (<see cref="FtpEncryptionMode.Explicit" />). The server certificate is validated against
///     the system trust store — FluentFTP's default
///     (<see cref="FluentFTP.FtpConfig.ValidateAnyCertificate" /> stays <see langword="false" />).
/// </remarks>
public sealed class DefaultAsyncFtpClientFactory(FtpStorageOptions options) : IAsyncFtpClientFactory
{
    /// <inheritdoc />
    public IAsyncFtpClient Create()
    {
        var config = new FtpConfig
        {
            EncryptionMode = options.UseSsl ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
        };

        return new AsyncFtpClient(options.Host, options.Username, options.Password ?? string.Empty, options.Port, config);
    }
}
