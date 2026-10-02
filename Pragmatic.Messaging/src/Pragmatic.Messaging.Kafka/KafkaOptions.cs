namespace Pragmatic.Messaging.Kafka;

/// <summary>
///     Configuration for the Kafka transport.
/// </summary>
public sealed class KafkaOptions
{
    /// <summary>Kafka bootstrap servers (e.g., "localhost:9092").</summary>
    public required string BootstrapServers { get; set; }

    /// <summary>Consumer group ID. Defaults to boundary name.</summary>
    public string? GroupId { get; set; }

    /// <summary>Enable idempotent producer for exactly-once semantics. Default: true.</summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>Auto-commit offset after handler success. Default: false (manual commit).</summary>
    public bool EnableAutoCommit { get; set; }

    /// <summary>Auto offset reset strategy. Default: "earliest".</summary>
    public string AutoOffsetReset { get; set; } = "earliest";

    /// <summary>Max poll interval in milliseconds. Default: 300000 (5 min).</summary>
    public int MaxPollIntervalMs { get; set; } = 300000;

    /// <summary>Session timeout in milliseconds. Default: 45000.</summary>
    public int SessionTimeoutMs { get; set; } = 45000;

    /// <summary>
    ///     Publishes messages whose handler threw to a dead-letter topic
    ///     (<c>"{topic}{DeadLetterTopicSuffix}"</c>) and then commits the offset, so the failure is
    ///     accounted instead of being silently absorbed by a later commit on the same partition.
    ///     Default: true. When disabled, a failed message's offset is skipped in-session and is
    ///     implicitly committed by the next successful message — i.e. it is LOST.
    /// </summary>
    public bool EnableDeadLetter { get; set; } = true;

    /// <summary>Suffix appended to the source topic to form the dead-letter topic. Default: ".dlq".</summary>
    public string DeadLetterTopicSuffix { get; set; } = ".dlq";

    /// <summary>
    ///     Creates topics explicitly via AdminClient before first use (publish, subscribe,
    ///     dead-letter), so the transport works against production brokers where
    ///     <c>auto.create.topics.enable</c> is off. Default: true.
    /// </summary>
    public bool AutoCreateTopics { get; set; } = true;

    /// <summary>Partition count for auto-created topics. Default: 3.</summary>
    public int DefaultPartitions { get; set; } = 3;

    /// <summary>Replication factor for auto-created topics. Default: -1 (broker default).</summary>
    public short ReplicationFactor { get; set; } = -1;
}
