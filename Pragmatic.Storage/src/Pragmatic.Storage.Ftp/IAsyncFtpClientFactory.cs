using FluentFTP;

namespace Pragmatic.Storage.Ftp;

/// <summary>
///     Creates a fresh <see cref="IAsyncFtpClient" /> for a single storage operation.
/// </summary>
/// <remarks>
///     <para>
///         An FTP connection is stateful and not thread-safe, while <see cref="FtpFileStorage" /> is a
///         shared singleton. Each operation therefore creates, connects, uses and disposes its own
///         client rather than sharing one across concurrent callers. This factory is the seam that
///         produces those clients; the default implementation builds one from
///         <see cref="FtpStorageOptions" />.
///     </para>
///     <para>
///         Replace it to customise client creation — for example to supply a custom certificate
///         validation callback for FTPS, or a connection pool.
///     </para>
/// </remarks>
public interface IAsyncFtpClientFactory
{
    /// <summary>
    ///     Creates a new, not-yet-connected <see cref="IAsyncFtpClient" />.
    /// </summary>
    /// <returns>A disposable client the caller must connect and dispose.</returns>
    IAsyncFtpClient Create();
}
