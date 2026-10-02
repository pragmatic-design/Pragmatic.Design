namespace Pragmatic.Storage.Sftp;

/// <summary>
///     Configuration for an SFTP (SSH File Transfer Protocol) storage backend.
/// </summary>
/// <remarks>
///     <para>
///         Exactly one authentication method must be supplied: set <see cref="Password" /> for
///         password authentication, or <see cref="PrivateKeyPath" /> (optionally with
///         <see cref="PrivateKeyPassphrase" />) for public-key authentication. If neither is set the
///         provider throws when it first tries to open a connection.
///     </para>
/// </remarks>
public sealed class SftpStorageOptions
{
    /// <summary>SFTP server host name or IP address.</summary>
    public required string Host { get; set; }

    /// <summary>SFTP server port. Defaults to the standard SSH port <c>22</c>.</summary>
    public int Port { get; set; } = 22;

    /// <summary>User name used to authenticate against the server.</summary>
    public required string Username { get; set; }

    /// <summary>
    ///     Password for password authentication. Provide this <em>or</em>
    ///     <see cref="PrivateKeyPath" /> — not necessarily both.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    ///     Path to a private key file (e.g. an OpenSSH / PEM key) for public-key authentication.
    ///     Provide this <em>or</em> <see cref="Password" />.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    ///     Passphrase protecting the private key referenced by <see cref="PrivateKeyPath" />,
    ///     or <see langword="null" /> when the key is not encrypted.
    /// </summary>
    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>
    ///     Remote root directory under which containers and files are created (POSIX path).
    ///     Defaults to <c>"/"</c>. A stored file lives at <c>{BasePath}/{container}/{storedName}</c>.
    /// </summary>
    public string BasePath { get; set; } = "/";

    /// <summary>
    ///     Maximum accepted size of a single uploaded file, in bytes.
    ///     <c>0</c> (default) means no limit. Set a positive value to reject oversized uploads
    ///     before (seekable) or while (non-seekable) streaming them to the server.
    /// </summary>
    public long MaxFileSizeBytes { get; set; }
}
