namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks the property whose value partitions this message on partitioned transports (Kafka):
///     messages with the same key land on the same partition, giving per-key ordering. The SG emits
///     a typed <see cref="IPartitionKeyResolver"/> (zero reflection); the publisher stamps the value
///     into the <c>x-partition-key</c> header and Kafka uses it as the message key.
/// </summary>
/// <remarks>
///     Without <c>[PartitionKey]</c> the Kafka key falls back to CorrelationId ?? MessageId
///     (per-correlation ordering). One property per message type; on non-partitioned transports
///     the header is carried but has no routing effect. Write it on a declared property or on a
///     positional record parameter — <c>[property: PartitionKey]</c> — whichever the message already
///     is; the resolver is generated in the assembly that declares the message, which is the one the
///     publisher references.
///     <para>
///         ⚠️ <c>ForAttributeWithMetadataName</c> does not surface the positional form, but that is not
///         a limit on it: the attribute lands on the property the parameter synthesizes, where a symbol
///         scan reads it — the same way <c>[CorrelationKey]</c> works on the same parameter.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PartitionKeyAttribute : Attribute;
