using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Diagnostics;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Enhanced batching provider with back-pressure policies, telemetry, and adaptive batch sizing.
/// </summary>
public abstract class BatchingProvider : PragmaticLoggerProviderBase, IAsyncFlushable
{
    private readonly Channel<PrioritizedLogEntry> _channel;
    private readonly ChannelWriter<PrioritizedLogEntry> _writer;
    private readonly ChannelReader<PrioritizedLogEntry> _reader;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly PragmaticLoggingEventCounters _eventCounters;

    // Back-pressure management
    private readonly ConcurrentQueue<LogEntry> _overflowBuffer;
    private readonly object _adaptiveBatchLock = new();
    private volatile int _currentBatchSize;
    private double _processingLatencyMs;

    // Metrics and telemetry
    private long _totalEntriesReceived;
    private long _totalEntriesDropped;
    private long _totalBatchesProcessed;
    private volatile int _currentQueueSize;
    private volatile int _peakQueueSize;
    private volatile int _consecutiveErrors;

    // Adaptive sizing
    private readonly ThroughputMeasurement _throughputMeasurement;
    private DateTime _lastAdaptiveAdjustment = DateTime.UtcNow;

    /// <summary>
    /// Represents a log entry with priority information for back-pressure handling.
    /// </summary>
    protected readonly struct PrioritizedLogEntry(LogEntry logEntry, int priority = 0)
    {
        public LogEntry LogEntry { get; } = logEntry;
        public int Priority { get; } = priority;
        public DateTime EnqueueTime { get; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Back-pressure policy configuration.
    /// </summary>
    protected class BackPressurePolicy
    {
        public BackPressureStrategy Strategy { get; set; } = BackPressureStrategy.DropLowPriority;
        public int HighPriorityThreshold { get; set; } = 80; // % of capacity
        public int CriticalThreshold { get; set; } = 95; // % of capacity  
        public int MaxOverflowBufferSize { get; set; } = 1000;
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Back-pressure strategies for handling queue overflow.
    /// </summary>
    public enum BackPressureStrategy
    {
        DropLowPriority,
        DropOldest,
        DropNewest,
        SelectiveDrop,
        Block
    }

    /// <summary>
    /// Throughput measurement helper.
    /// </summary>
    private class ThroughputMeasurement
    {
        private readonly Queue<(DateTime timestamp, long count)> _samples = new();
        private readonly object _lock = new();
        private long _totalCount;

        public void RecordSample(long count)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _samples.Enqueue((now, count));
                _totalCount += count;

                // Keep only last 60 seconds of samples
                while (_samples.Count > 0 && now - _samples.Peek().timestamp > TimeSpan.FromSeconds(60))
                {
                    _samples.Dequeue();
                }
            }
        }

        public double GetThroughputPerSecond()
        {
            lock (_lock)
            {
                if (_samples.Count < 2)
                    return 0;

                var timeSpan = _samples.Last().timestamp - _samples.First().timestamp;
                if (timeSpan.TotalSeconds < 1)
                    return 0;

                var totalInPeriod = _samples.Sum(s => s.count);
                return totalInPeriod / timeSpan.TotalSeconds;
            }
        }
    }

    protected BatchingProvider(string name, IPragmaticProviderConfiguration configuration)
        : base(name, configuration)
    {
        if (!configuration.Performance.EnableBatching)
        {
            throw new ArgumentException("Batching provider requires EnableBatching to be true", nameof(configuration));
        }

        // Initialize EventCounters for telemetry
        _eventCounters = new PragmaticLoggingEventCounters(name);

        // Initialize adaptive batch sizing
        _currentBatchSize = configuration.Performance.BatchSize;
        _throughputMeasurement = new ThroughputMeasurement();

        // Initialize overflow buffer
        _overflowBuffer = new ConcurrentQueue<LogEntry>();

        // Create channel with enhanced options
        var channelOptions = new BoundedChannelOptions(configuration.Performance.MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropWrite, // We'll handle overflow ourselves
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        _channel = Channel.CreateBounded<PrioritizedLogEntry>(channelOptions);
        _writer = _channel.Writer;
        _reader = _channel.Reader;

        _cancellationTokenSource = new CancellationTokenSource();

        // Start background processing task
        _processingTask = Task.Run(ProcessLogEntriesAsync, _cancellationTokenSource.Token);
    }

    /// <inheritdoc />
    protected override void WriteLogCore(LogEntry logEntry)
    {
        Interlocked.Increment(ref _totalEntriesReceived);

        var priority = CalculatePriority(logEntry);
        var prioritizedEntry = new PrioritizedLogEntry(logEntry, priority);

        // Try to write to main channel
        if (_writer.TryWrite(prioritizedEntry))
        {
            var currentSize = Interlocked.Increment(ref _currentQueueSize);
            UpdatePeakQueueSize(currentSize);
            return;
        }

        // Main channel is full - apply back-pressure policy
        HandleBackPressure(logEntry, priority);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
            return;

        await _flushSemaphore.WaitAsync(cancellationToken);
        try
        {
            await FlushCurrentBatchAsync(cancellationToken);
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    /// <inheritdoc />
    protected override Dictionary<string, object?> GetCustomMetrics()
    {
        var baseMetrics = base.GetCustomMetrics();

        var throughput = _throughputMeasurement.GetThroughputPerSecond();
        var dropRate = _totalEntriesReceived > 0 ? (_totalEntriesDropped * 100.0) / _totalEntriesReceived : 0;

        baseMetrics["CurrentQueueSize"] = _currentQueueSize;
        baseMetrics["PeakQueueSize"] = _peakQueueSize;
        baseMetrics["CurrentBatchSize"] = _currentBatchSize;
        baseMetrics["ConfiguredBatchSize"] = Configuration.Performance.BatchSize;
        baseMetrics["TotalEntriesReceived"] = _totalEntriesReceived;
        baseMetrics["TotalEntriesDropped"] = _totalEntriesDropped;
        baseMetrics["DropRate"] = dropRate;
        baseMetrics["TotalBatchesProcessed"] = _totalBatchesProcessed;
        baseMetrics["ThroughputPerSecond"] = throughput;
        baseMetrics["AverageProcessingLatency"] = _processingLatencyMs;
        baseMetrics["ConsecutiveErrors"] = _consecutiveErrors;
        baseMetrics["OverflowBufferSize"] = _overflowBuffer.Count;

        return baseMetrics;
    }

    /// <inheritdoc />
    protected override ProviderHealthStatus PerformCustomHealthCheck()
    {
        if (_cancellationTokenSource.IsCancellationRequested)
            return ProviderHealthStatus.Unhealthy;

        if (_consecutiveErrors > 10)
            return ProviderHealthStatus.Unhealthy;

        var capacity = Configuration.Performance.MaxQueueSize;
        var utilizationPercent = (double)_currentQueueSize / capacity * 100;

        if (utilizationPercent > 95)
            return ProviderHealthStatus.Degraded;

        if (_consecutiveErrors > 3)
            return ProviderHealthStatus.Degraded;

        return ProviderHealthStatus.Healthy;
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        // Signal shutdown
        _writer.Complete();
        _cancellationTokenSource.Cancel();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Ignore shutdown errors
        }

        _eventCounters?.Dispose();
        _cancellationTokenSource.Dispose();
        _flushSemaphore.Dispose();

        base.DisposeCore();
    }

    /// <summary>
    /// Writes a batch of log entries. Must be implemented by derived classes.
    /// </summary>
    protected abstract Task WriteBatchAsync(IReadOnlyList<LogEntry> logEntries, CancellationToken cancellationToken);

    /// <summary>
    /// Flushes any pending data. Override to implement provider-specific flushing.
    /// </summary>
    protected virtual Task FlushCurrentBatchAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task ProcessLogEntriesAsync()
    {
        var cancellationToken = _cancellationTokenSource.Token;
        var batch = new List<LogEntry>();
        var flushInterval = Configuration.Performance.FlushInterval;
        var lastFlushTime = DateTime.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var processingStart = DateTime.UtcNow;
                    batch.Clear();

                    // Try to fill batch from main channel
                    await FillBatchFromChannel(batch, cancellationToken);

                    // Try to fill remaining space from overflow buffer
                    FillBatchFromOverflow(batch);

                    if (batch.Count > 0)
                    {
                        var batchStart = DateTime.UtcNow;

                        await WriteBatchAsync(batch, cancellationToken);

                        var batchProcessingTime = DateTime.UtcNow - batchStart;

                        // Update metrics
                        Interlocked.Increment(ref _totalBatchesProcessed);
                        _processingLatencyMs = batchProcessingTime.TotalMilliseconds;
                        _throughputMeasurement.RecordSample(batch.Count);

                        // Update EventCounters (legacy) + OTel Meter
                        _eventCounters.RecordBatch(batch.Count, batchProcessingTime.TotalMilliseconds);
                        LoggingDiagnostics.LogThroughput.Add(batch.Count);
                        LoggingDiagnostics.BatchLatency.Record(batchProcessingTime.TotalMilliseconds);

                        // Reset consecutive errors on success
                        Interlocked.Exchange(ref _consecutiveErrors, 0);

                        // Adaptive batch sizing
                        AdaptBatchSizeIfNeeded(batch.Count, batchProcessingTime);
                    }

                    // Check if we need to flush based on time
                    var now = DateTime.UtcNow;
                    if (now - lastFlushTime >= flushInterval)
                    {
                        await FlushCurrentBatchAsync(cancellationToken);
                        lastFlushTime = now;
                    }

                    // Sleep briefly if no work was done
                    if (batch.Count == 0)
                    {
                        await Task.Delay(10, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _consecutiveErrors);
                    OnWriteError(null!, ex);

                    // Back off on errors
                    await Task.Delay(Math.Min(1000 * _consecutiveErrors, 10000), CancellationToken.None);
                }
            }
        }
        finally
        {
            await ProcessRemainingEntries();
        }
    }

    private async Task FillBatchFromChannel(List<LogEntry> batch, CancellationToken cancellationToken)
    {
        var targetBatchSize = _currentBatchSize;

        // Wait for at least one item
        if (await _reader.WaitToReadAsync(cancellationToken))
        {
            // Collect items for batch
            while (batch.Count < targetBatchSize && _reader.TryRead(out var prioritizedEntry))
            {
                batch.Add(prioritizedEntry.LogEntry);
                Interlocked.Decrement(ref _currentQueueSize);
            }
        }
    }

    private void FillBatchFromOverflow(List<LogEntry> batch)
    {
        var targetBatchSize = _currentBatchSize;

        while (batch.Count < targetBatchSize && _overflowBuffer.TryDequeue(out var logEntry))
        {
            batch.Add(logEntry);
        }
    }

    private async Task ProcessRemainingEntries()
    {
        var batch = new List<LogEntry>();

        // Process remaining items in main channel
        while (_reader.TryRead(out var prioritizedEntry))
        {
            batch.Add(prioritizedEntry.LogEntry);
            if (batch.Count >= _currentBatchSize * 2) // Larger final batch
            {
                try
                {
                    await WriteBatchAsync(batch, CancellationToken.None);
                    batch.Clear();
                }
                catch (Exception ex)
                {
                    OnWriteError(null!, ex);
                }
            }
        }

        // Process remaining items in overflow buffer
        while (_overflowBuffer.TryDequeue(out var logEntry) && batch.Count < _currentBatchSize * 2)
        {
            batch.Add(logEntry);
        }

        // Write final batch
        if (batch.Count > 0)
        {
            try
            {
                await WriteBatchAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                OnWriteError(null!, ex);
            }
        }
    }

    private void HandleBackPressure(LogEntry logEntry, int priority)
    {
        var policy = GetBackPressurePolicy();
        var capacity = Configuration.Performance.MaxQueueSize;
        var currentUtilization = (double)_currentQueueSize / capacity * 100;

        switch (policy.Strategy)
        {
            case BackPressureStrategy.DropLowPriority:
                if (priority >= GetHighPriorityThreshold() || currentUtilization < policy.HighPriorityThreshold)
                {
                    TryAddToOverflowBuffer(logEntry);
                }
                else
                {
                    DropEntry(logEntry, "LowPriority");
                }
                break;

            case BackPressureStrategy.DropOldest:
                if (!TryReplaceOldestInChannel())
                {
                    TryAddToOverflowBuffer(logEntry);
                }
                break;

            case BackPressureStrategy.SelectiveDrop:
                if (ShouldKeepEntry(logEntry, currentUtilization))
                {
                    TryAddToOverflowBuffer(logEntry);
                }
                else
                {
                    DropEntry(logEntry, "Selective");
                }
                break;

            case BackPressureStrategy.Block:
                // Try to add to overflow buffer, but this is blocking behavior
                TryAddToOverflowBuffer(logEntry);
                break;

            case BackPressureStrategy.DropNewest:
            default:
                DropEntry(logEntry, "Newest");
                break;
        }
    }

    private bool TryAddToOverflowBuffer(LogEntry logEntry)
    {
        var policy = GetBackPressurePolicy();

        if (_overflowBuffer.Count >= policy.MaxOverflowBufferSize)
        {
            DropEntry(logEntry, "OverflowBufferFull");
            return false;
        }

        _overflowBuffer.Enqueue(logEntry);
        return true;
    }

    private bool TryReplaceOldestInChannel()
    {
        // This is complex with channels - for now, just drop
        return false;
    }

    private bool ShouldKeepEntry(LogEntry logEntry, double currentUtilization)
    {
        var policy = GetBackPressurePolicy();

        // Always keep high-priority entries
        if (CalculatePriority(logEntry) >= GetHighPriorityThreshold())
            return true;

        // Drop more aggressively as utilization increases
        var dropProbability = Math.Max(0, (currentUtilization - policy.HighPriorityThreshold) /
                                          (policy.CriticalThreshold - policy.HighPriorityThreshold));

        return Random.Shared.NextDouble() > dropProbability;
    }

    private void DropEntry(LogEntry logEntry, string reason)
    {
        Interlocked.Increment(ref _totalEntriesDropped);
        _eventCounters.RecordDrop(reason);
        LoggingDiagnostics.LogDrops.Add(1,
            new KeyValuePair<string, object?>("reason", reason));
    }

    private void AdaptBatchSizeIfNeeded(int actualBatchSize, TimeSpan processingTime)
    {
        var now = DateTime.UtcNow;
        if (now - _lastAdaptiveAdjustment < TimeSpan.FromSeconds(30))
            return; // Don't adapt too frequently

        lock (_adaptiveBatchLock)
        {
            if (now - _lastAdaptiveAdjustment < TimeSpan.FromSeconds(30))
                return;

            _lastAdaptiveAdjustment = now;

            var throughput = _throughputMeasurement.GetThroughputPerSecond();
            var targetThroughput = GetCustomProperty<double>("TargetThroughput", 1000.0);
            var maxBatchSize = GetCustomProperty<int>("MaxAdaptiveBatchSize", Configuration.Performance.BatchSize * 4);
            var minBatchSize = GetCustomProperty<int>("MinAdaptiveBatchSize", Configuration.Performance.BatchSize / 4);

            var newBatchSize = _currentBatchSize;

            if (throughput < targetThroughput * 0.8 && processingTime.TotalMilliseconds < 100)
            {
                // Throughput too low and processing fast - increase batch size
                newBatchSize = Math.Min(maxBatchSize, (int)(_currentBatchSize * 1.2));
            }
            else if (throughput > targetThroughput * 1.2 || processingTime.TotalMilliseconds > 500)
            {
                // Throughput too high or processing slow - decrease batch size  
                newBatchSize = Math.Max(minBatchSize, (int)(_currentBatchSize * 0.8));
            }

            if (newBatchSize != _currentBatchSize)
            {
                _currentBatchSize = newBatchSize;
                _eventCounters.RecordBatchSizeChange(_currentBatchSize);
                LoggingDiagnostics.BatchSize.Record(_currentBatchSize);
            }
        }
    }

    private int CalculatePriority(LogEntry logEntry)
    {
        // Higher number = higher priority
        return logEntry.LogLevel switch
        {
            LogLevel.Critical => 100,
            LogLevel.Error => 80,
            LogLevel.Warning => 60,
            LogLevel.Information => 40,
            LogLevel.Debug => 20,
            LogLevel.Trace => 10,
            _ => 0
        };
    }

    private int GetHighPriorityThreshold()
    {
        return GetCustomProperty<int>("HighPriorityThreshold", 70);
    }

    private BackPressurePolicy GetBackPressurePolicy()
    {
        return new BackPressurePolicy
        {
            Strategy = GetCustomProperty<BackPressureStrategy>("BackPressureStrategy", BackPressureStrategy.DropLowPriority),
            HighPriorityThreshold = GetCustomProperty<int>("HighPriorityThreshold", 80),
            CriticalThreshold = GetCustomProperty<int>("CriticalThreshold", 95),
            MaxOverflowBufferSize = GetCustomProperty<int>("MaxOverflowBufferSize", 1000),
            MaxAge = TimeSpan.FromMinutes(GetCustomProperty<int>("MaxAgeMinutes", 5))
        };
    }

    private void UpdatePeakQueueSize(int currentSize)
    {
        var currentPeak = _peakQueueSize;
        while (currentSize > currentPeak)
        {
            var original = Interlocked.CompareExchange(ref _peakQueueSize, currentSize, currentPeak);
            if (original == currentPeak)
                break;
            currentPeak = original;
        }
    }

    private T GetCustomProperty<T>(string key, T defaultValue)
    {
        if (Configuration.CustomProperties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }
}

/// <summary>
/// EventCounters for telemetry data collection.
/// </summary>
internal sealed class PragmaticLoggingEventCounters : EventSource, IDisposable
{
    private readonly EventCounter _throughputCounter;
    private readonly EventCounter _latencyCounter;
    private readonly EventCounter _dropCounter;
    private readonly EventCounter _queueUtilizationCounter;
    private readonly EventCounter _batchSizeCounter;

    private volatile bool _disposed;

    public PragmaticLoggingEventCounters(string providerName) : base($"Pragmatic.Logging.{providerName}")
    {
        _throughputCounter = new EventCounter("log-throughput", this)
        {
            DisplayName = "Log Throughput",
            DisplayUnits = "entries/sec"
        };

        _latencyCounter = new EventCounter("batch-processing-latency", this)
        {
            DisplayName = "Batch Processing Latency",
            DisplayUnits = "ms"
        };

        _dropCounter = new EventCounter("dropped-entries", this)
        {
            DisplayName = "Dropped Log Entries",
            DisplayUnits = "count"
        };

        _queueUtilizationCounter = new EventCounter("queue-utilization", this)
        {
            DisplayName = "Queue Utilization",
            DisplayUnits = "%"
        };

        _batchSizeCounter = new EventCounter("adaptive-batch-size", this)
        {
            DisplayName = "Adaptive Batch Size",
            DisplayUnits = "entries"
        };
    }

    public void RecordBatch(int entryCount, double processingLatencyMs)
    {
        if (_disposed)
            return;

        _throughputCounter?.WriteMetric(entryCount);
        _latencyCounter?.WriteMetric(processingLatencyMs);
    }

    public void RecordDrop(string reason)
    {
        if (_disposed)
            return;

        _dropCounter?.WriteMetric(1);
        WriteEvent(1, reason);
    }

    public void RecordQueueUtilization(double utilizationPercent)
    {
        if (_disposed)
            return;

        _queueUtilizationCounter?.WriteMetric(utilizationPercent);
    }

    public void RecordBatchSizeChange(int newBatchSize)
    {
        if (_disposed)
            return;

        _batchSizeCounter?.WriteMetric(newBatchSize);
        WriteEvent(2, newBatchSize);
    }

    [Event(1, Message = "Log entry dropped: {0}", Level = EventLevel.Warning)]
    private void LogEntryDropped(string reason) { }

    [Event(2, Message = "Batch size adapted to: {0}", Level = EventLevel.Informational)]
    private void BatchSizeChanged(int newSize) { }

    public new void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _throughputCounter?.Dispose();
        _latencyCounter?.Dispose();
        _dropCounter?.Dispose();
        _queueUtilizationCounter?.Dispose();
        _batchSizeCounter?.Dispose();

        base.Dispose();
        GC.SuppressFinalize(this);
    }
}