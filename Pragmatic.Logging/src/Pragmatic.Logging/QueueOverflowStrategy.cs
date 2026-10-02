namespace Pragmatic.Logging;

/// <summary>
/// Defines strategies for handling queue overflow when the logging provider's internal queue reaches capacity.
/// These strategies help prevent memory exhaustion while maintaining system stability under high load conditions.
/// </summary>
/// <remarks>
/// <para>
/// Queue overflow scenarios occur when log entries are being generated faster than they can be processed
/// and written to their destination. Different strategies provide different trade-offs between:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Data preservation</strong> - How much log data is retained</description></item>
/// <item><description><strong>Memory usage</strong> - Impact on application memory consumption</description></item>  
/// <item><description><strong>Performance</strong> - Impact on application throughput</description></item>
/// <item><description><strong>Latency</strong> - Impact on application response times</description></item>
/// </list>
/// </remarks>
/// <example>
/// Configuration examples for different scenarios:
/// <code>
/// // High-throughput scenario: Drop oldest entries to maintain recent logs
/// config.Performance.QueueOverflowStrategy = QueueOverflowStrategy.DropOldest;
/// config.Performance.MaxQueueSize = 50000;
/// 
/// // Critical system: Block briefly to preserve all logs
/// config.Performance.QueueOverflowStrategy = QueueOverflowStrategy.Block;
/// config.Performance.MaxQueueSize = 10000;
/// config.Performance.MaxBlockTimeoutMs = 100;
/// 
/// // High-availability scenario: Drop new entries to maintain performance
/// config.Performance.QueueOverflowStrategy = QueueOverflowStrategy.DropNewest;
/// config.Performance.MaxQueueSize = 25000;
/// </code>
/// </example>
public enum QueueOverflowStrategy
{
    /// <summary>
    /// Drop the oldest entries in the queue to make room for new ones.
    /// This strategy prioritizes recent log data over historical data.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Recent logs are more important than historical logs</description></item>
    /// <item><description>You want to maintain a sliding window of recent activity</description></item>
    /// <item><description>Memory usage needs to be bounded strictly</description></item>
    /// </list>
    /// <para><strong>Characteristics:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Memory usage:</strong> Bounded and predictable</description></item>
    /// <item><description><strong>Performance impact:</strong> Minimal - no blocking</description></item>
    /// <item><description><strong>Data loss:</strong> Historical data is lost first</description></item>
    /// </list>
    /// </remarks>
    DropOldest,

    /// <summary>
    /// Drop the newest entries when the queue is full, preserving older entries.
    /// This strategy prioritizes preserving the chronological order of events.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Maintaining chronological sequence is critical</description></item>
    /// <item><description>Historical context is more valuable than recent spikes</description></item>
    /// <item><description>You want to preserve the start of an event sequence</description></item>
    /// </list>
    /// <para><strong>Characteristics:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Memory usage:</strong> Bounded and predictable</description></item>
    /// <item><description><strong>Performance impact:</strong> Minimal - no blocking</description></item>
    /// <item><description><strong>Data loss:</strong> Recent data is lost during spikes</description></item>
    /// </list>
    /// </remarks>
    DropNewest,

    /// <summary>
    /// Block the calling thread briefly when the queue is full, waiting for space to become available.
    /// This strategy attempts to preserve all log data at the cost of potential performance impact.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>No log data can be lost</description></item>
    /// <item><description>Brief performance impacts are acceptable</description></item>
    /// <item><description>The logging load spikes are temporary</description></item>
    /// </list>
    /// <para><strong>Characteristics:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Memory usage:</strong> Can grow beyond queue limits temporarily</description></item>
    /// <item><description><strong>Performance impact:</strong> Can block application threads</description></item>
    /// <item><description><strong>Data loss:</strong> Minimal if timeout is sufficient</description></item>
    /// </list>
    /// <para><strong>Configuration options:</strong></para>
    /// <list type="bullet">
    /// <item><description><c>MaxBlockTimeoutMs</c> - Maximum time to wait for queue space</description></item>
    /// <item><description><c>BlockingRetryCount</c> - Number of retry attempts before dropping</description></item>
    /// </list>
    /// </remarks>
    Block,

    /// <summary>
    /// Use an intelligent dropping strategy that considers log level, source, and content to decide what to drop.
    /// This strategy attempts to preserve the most important log entries during overflow conditions.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Log entries have different importance levels</description></item>
    /// <item><description>You want to preserve errors and warnings over debug messages</description></item>
    /// <item><description>Intelligent filtering is more valuable than simple FIFO/LIFO</description></item>
    /// </list>
    /// <para><strong>Selection criteria (in priority order):</strong></para>
    /// <list type="number">
    /// <item><description><strong>Log Level:</strong> Critical > Error > Warning > Information > Debug > Trace</description></item>
    /// <item><description><strong>Exception presence:</strong> Entries with exceptions are preserved</description></item>
    /// <item><description><strong>Category importance:</strong> Configurable category priorities</description></item>
    /// <item><description><strong>Recency:</strong> Among equal priority, newer entries are preferred</description></item>
    /// </list>
    /// <para><strong>Characteristics:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Memory usage:</strong> Bounded with intelligent content management</description></item>
    /// <item><description><strong>Performance impact:</strong> Slight overhead for priority evaluation</description></item>
    /// <item><description><strong>Data loss:</strong> Least important entries are dropped first</description></item>
    /// </list>
    /// </remarks>
    DropByPriority,

    /// <summary>
    /// Immediately discard any new entries when the queue is full without any queuing or blocking.
    /// This strategy prioritizes application performance over log data completeness.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Application performance is more critical than complete logging</description></item>
    /// <item><description>Logging must never impact application throughput</description></item>
    /// <item><description>You have alternative monitoring/alerting systems</description></item>
    /// </list>
    /// <para><strong>Characteristics:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Memory usage:</strong> Strictly bounded</description></item>
    /// <item><description><strong>Performance impact:</strong> Minimal - immediate return</description></item>
    /// <item><description><strong>Data loss:</strong> High during overflow conditions</description></item>
    /// </list>
    /// </remarks>
    DropImmediate,

    /// <summary>
    /// Allow the queue to grow beyond its configured size, potentially using additional memory.
    /// This strategy prioritizes data preservation over memory consumption limits.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Memory is abundant and log data is critical</description></item>
    /// <item><description>Overflow conditions are rare and temporary</description></item>
    /// <item><description>You have memory monitoring and can handle OOM conditions</description></item>
    /// </list>
    /// <para><strong>Characteristics:</strong></para>
    /// <list type="bullet">
    /// <item><description><strong>Memory usage:</strong> Can grow unbounded during spikes</description></item>
    /// <item><description><strong>Performance impact:</strong> None until memory pressure occurs</description></item>
    /// <item><description><strong>Data loss:</strong> None, but risk of OutOfMemoryException</description></item>
    /// </list>
    /// <para><strong>Safety considerations:</strong></para>
    /// <list type="bullet">
    /// <item><description>Monitor memory usage and have fallback strategies</description></item>
    /// <item><description>Consider using with memory pressure detection</description></item>
    /// <item><description>Implement circuit breaker patterns for extreme cases</description></item>
    /// </list>
    /// </remarks>
    AllowGrowth
}