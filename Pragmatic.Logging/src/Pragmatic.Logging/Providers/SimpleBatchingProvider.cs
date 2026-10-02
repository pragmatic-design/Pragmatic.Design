namespace Pragmatic.Logging.Providers;

/// <summary>
/// Simple batching provider for testing and demonstrations.
/// Provides a concrete implementation of the BatchingProvider for easy usage.
/// </summary>
public sealed class SimpleBatchingProvider : BatchingProvider
{
    private readonly Action<IReadOnlyList<LogEntry>> _writeBatch;

    /// <summary>
    /// Initializes a new instance of SimpleBatchingProvider.
    /// </summary>
    /// <param name="name">Provider name</param>
    /// <param name="configuration">Provider configuration</param>
    /// <param name="writeBatch">Delegate to handle batch writing</param>
    public SimpleBatchingProvider(
        string name,
        IPragmaticProviderConfiguration configuration,
        Action<IReadOnlyList<LogEntry>> writeBatch)
        : base(name, configuration)
    {
        _writeBatch = writeBatch ?? throw new ArgumentNullException(nameof(writeBatch));
    }

    /// <inheritdoc />
    protected override Task WriteBatchAsync(IReadOnlyList<LogEntry> logEntries, CancellationToken cancellationToken)
    {
        _writeBatch(logEntries);
        return Task.CompletedTask;
    }
}