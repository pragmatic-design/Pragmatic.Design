namespace Pragmatic.Messaging;

/// <summary>
///     Resolves the partition key of a message (from its <c>[PartitionKey]</c> property).
///     Implementations are SG-generated per module assembly — a compile-time switch over the
///     assembly's message types, zero reflection.
/// </summary>
public interface IPartitionKeyResolver
{
    /// <summary>The header the publisher stamps and partitioned transports read as message key.</summary>
    const string HeaderName = "x-partition-key";

    /// <summary>Returns the partition key for <paramref name="message"/>, or null when the type has none.</summary>
    string? TryGetPartitionKey(object message);
}
