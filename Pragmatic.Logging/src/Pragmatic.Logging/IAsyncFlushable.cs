namespace Pragmatic.Logging;

/// <summary>
/// Defines an interface for providers that support asynchronous flushing of buffered data.
/// This interface allows for explicit flushing of pending log entries, ensuring data persistence
/// before application shutdown or at specific intervals.
/// </summary>
/// <remarks>
/// <para>
/// Providers implementing this interface can be used with flush operations to ensure
/// that all buffered log entries are written to their final destination. This is particularly
/// important for:
/// </para>
/// <list type="bullet">
/// <item><description>File-based providers that buffer writes for performance</description></item>
/// <item><description>Network-based providers with batching capabilities</description></item>
/// <item><description>Database providers using transaction batching</description></item>
/// <item><description>Cloud providers with async upload queues</description></item>
/// </list>
/// </remarks>
/// <example>
/// Usage in application shutdown:
/// <code>
/// public async Task StopAsync(CancellationToken cancellationToken)
/// {
///     if (logger.Provider is IAsyncFlushable flushableProvider)
///     {
///         await flushableProvider.FlushAsync(cancellationToken);
///     }
/// }
/// </code>
/// 
/// Usage with periodic flushing:
/// <code>
/// // Periodic flush every 30 seconds
/// using var timer = new Timer(async _ =>
/// {
///     if (provider is IAsyncFlushable flushable)
///     {
///         await flushable.FlushAsync(CancellationToken.None);
///     }
/// }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
/// </code>
/// </example>
public interface IAsyncFlushable
{
    /// <summary>
    /// Asynchronously flushes all buffered log entries to their destination.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the flush operation.
    /// Note that cancellation may result in some buffered entries not being flushed.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous flush operation.
    /// The task completes when all currently buffered entries have been written.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method should:
    /// - Write all currently buffered entries to their destination
    /// - Complete synchronously if no buffering is used
    /// - Be thread-safe and support concurrent calls
    /// - Handle cancellation gracefully without losing critical data
    /// </para>
    /// <para>
    /// Implementations should ensure that after successful completion,
    /// all log entries submitted before the flush call have been persisted
    /// to their final destination (file, database, network, etc.).
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the operation is cancelled via the cancellation token.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the provider is disposed or in an invalid state for flushing.
    /// </exception>
    Task FlushAsync(CancellationToken cancellationToken = default);
}