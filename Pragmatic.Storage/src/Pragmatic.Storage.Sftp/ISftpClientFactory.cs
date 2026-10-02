using Renci.SshNet;

namespace Pragmatic.Storage.Sftp;

/// <summary>
///     Creates a fresh, <strong>disconnected</strong> <see cref="ISftpClient" /> for a single
///     storage operation.
/// </summary>
/// <remarks>
///     <para>
///         SSH.NET's <see cref="SftpClient" /> is connection-based and <strong>not thread-safe</strong>,
///         while <see cref="SftpFileStorage" /> is registered as a singleton and used concurrently.
///         The provider therefore opens a client per operation via this factory, connects, runs the
///         operation, then disconnects and disposes — never sharing a client across threads.
///     </para>
///     <para>
///         Implement this interface to plug in custom client construction or connection pooling.
///         The default <c>SftpClientFactory</c> builds a client from
///         <see cref="SftpStorageOptions" /> (password or private-key authentication).
///     </para>
/// </remarks>
public interface ISftpClientFactory
{
    /// <summary>
    ///     Creates a new, disconnected <see cref="ISftpClient" />. The caller is responsible for
    ///     connecting, disposing, and never sharing the returned client across threads.
    /// </summary>
    ISftpClient Create();
}
