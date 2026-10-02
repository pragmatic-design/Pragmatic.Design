namespace Pragmatic.Storage.Ftp;

/// <summary>
///     Configuration for FTP / FTPS file storage.
/// </summary>
/// <remarks>
///     Enable <see cref="UseSsl" /> for <strong>explicit FTPS</strong> (FTP over TLS): the control
///     connection starts in plain text and is upgraded via <c>AUTH TLS</c>. The server certificate is
///     validated against the system trust store by default; a self-signed or otherwise untrusted
///     certificate will cause the connection to fail rather than be silently accepted.
/// </remarks>
public sealed class FtpStorageOptions
{
    /// <summary>FTP server host name or IP address.</summary>
    public required string Host { get; set; }

    /// <summary>FTP control port. Defaults to <c>21</c>.</summary>
    public int Port { get; set; } = 21;

    /// <summary>User name used to authenticate with the server.</summary>
    public required string Username { get; set; }

    /// <summary>
    ///     Password used to authenticate with the server, or <see langword="null" /> for an
    ///     anonymous / password-less login.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    ///     When <see langword="true" />, uses explicit FTPS (FTP over TLS). Defaults to
    ///     <see langword="false" /> (plain FTP). The server certificate is validated against the
    ///     system trust store.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    ///     Root directory on the server under which containers are created. Defaults to <c>/</c>.
    ///     Always interpreted with POSIX (<c>/</c>) separators.
    /// </summary>
    public string BasePath { get; set; } = "/";

    /// <summary>
    ///     Maximum accepted size of a single uploaded file, in bytes. <c>0</c> (default) means no
    ///     limit. Set a positive value to reject oversized uploads before streaming to the server.
    /// </summary>
    public long MaxFileSizeBytes { get; set; }
}
