using Renci.SshNet;

namespace Pragmatic.Storage.Sftp;

/// <summary>
///     Default <see cref="ISftpClientFactory" /> that builds a <see cref="SftpClient" /> from
///     <see cref="SftpStorageOptions" />, choosing private-key or password authentication.
/// </summary>
internal sealed class SftpClientFactory(SftpStorageOptions options) : ISftpClientFactory
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     Neither a password nor a private key path was configured.
    /// </exception>
    public ISftpClient Create()
    {
        if (!string.IsNullOrEmpty(options.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(options.PrivateKeyPassphrase)
                ? new PrivateKeyFile(options.PrivateKeyPath)
                : new PrivateKeyFile(options.PrivateKeyPath, options.PrivateKeyPassphrase);

            return new SftpClient(options.Host, options.Port, options.Username, keyFile);
        }

        if (!string.IsNullOrEmpty(options.Password))
            return new SftpClient(options.Host, options.Port, options.Username, options.Password);

        throw new InvalidOperationException(
            "SFTP storage requires an authentication method: set either Password or PrivateKeyPath on SftpStorageOptions.");
    }
}
