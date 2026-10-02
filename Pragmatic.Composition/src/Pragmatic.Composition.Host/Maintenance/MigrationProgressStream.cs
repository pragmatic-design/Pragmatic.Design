using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Bounded channel-based implementation of <see cref="IMigrationProgressStream" />.
///     Supports multiple concurrent consumers (e.g. SSE clients).
/// </summary>
public sealed partial class MigrationProgressStream : IMigrationProgressStream
{
    private readonly Channel<MigrationProgressEvent> _channel;
    private readonly ILogger _logger;

    /// <summary>Creates a bounded migration progress stream that drops the oldest live event when full.</summary>
    /// <param name="capacity">Bounded channel capacity for live consumers (history retains all events).</param>
    /// <param name="logger">Optional logger; a <see cref="NullLogger"/> is used when omitted.</param>
    public MigrationProgressStream(int capacity = 100, ILogger<MigrationProgressStream>? logger = null)
    {
        _logger = logger ?? NullLogger<MigrationProgressStream>.Instance;
        _channel = Channel.CreateBounded<MigrationProgressEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        });
    }

    /// <summary>
    ///     History of all events reported so far (for late-joining consumers).
    /// </summary>
    private readonly List<MigrationProgressEvent> _history = [];
    private readonly Lock _historyLock = new();

    /// <inheritdoc />
    /// <remarks>The event is always retained in history; it is additionally pushed to live consumers unless the stream is completed.</remarks>
    public void Report(MigrationProgressEvent progressEvent)
    {
        lock (_historyLock)
        {
            _history.Add(progressEvent);
        }

        // With DropOldest the channel never rejects a write because it is full — it evicts the oldest
        // instead. A false return therefore means the channel has been Completed (Report after
        // Complete()). The event is still retained in _history for late-joining consumers, so no data
        // is lost; we log it as a misuse signal rather than discarding the result silently.
        if (!_channel.Writer.TryWrite(progressEvent))
            LogWriteAfterComplete(progressEvent.Phase);
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "MigrationProgressStream.Report('{Phase}') after the stream was completed; event kept in history, not delivered to live consumers")]
    private partial void LogWriteAfterComplete(string phase);

    /// <inheritdoc />
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<MigrationProgressEvent> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>
    ///     Returns a snapshot of all events reported so far.
    /// </summary>
    public IReadOnlyList<MigrationProgressEvent> GetHistory()
    {
        lock (_historyLock)
        {
            return [.. _history];
        }
    }
}
