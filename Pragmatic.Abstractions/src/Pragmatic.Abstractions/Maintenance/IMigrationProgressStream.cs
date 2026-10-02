namespace Pragmatic.Maintenance;

/// <summary>
///     Producer/consumer stream for migration progress events.
///     Producers call <see cref="Report" />, consumers iterate via <see cref="StreamAsync" />.
/// </summary>
public interface IMigrationProgressStream
{
    /// <summary>
    ///     Reports a progress event (non-blocking, fire-and-forget).
    /// </summary>
    void Report(MigrationProgressEvent progressEvent);

    /// <summary>
    ///     Signals that no more events will be produced. Consumers will complete after draining.
    /// </summary>
    void Complete();

    /// <summary>
    ///     Signals a terminal failure: reports an error event
    ///     (<see cref="MigrationProgressEvent.IsError"/> = <c>true</c>) and then completes the
    ///     stream, so consumers see the failure as the last event before the stream ends.
    /// </summary>
    /// <param name="message">Human-readable failure summary.</param>
    /// <param name="exception">Optional exception; surfaced in <see cref="MigrationProgressEvent.ErrorDetail"/>.</param>
    /// <param name="databaseName">Database being processed, if applicable.</param>
    /// <remarks>
    ///     Default implementation composes <see cref="Report"/> + <see cref="Complete"/>; any
    ///     implementor gets correct behavior without changes. Override only to add transport-level
    ///     fault semantics.
    /// </remarks>
    void ReportFailure(string message, Exception? exception = null, string? databaseName = null)
    {
        Report(MigrationProgressEvent.Failed(message, exception, databaseName: databaseName));
        Complete();
    }

    /// <summary>
    ///     Streams progress events as they arrive. Completes when <see cref="Complete" /> is called.
    /// </summary>
    IAsyncEnumerable<MigrationProgressEvent> StreamAsync(CancellationToken cancellationToken = default);
}
