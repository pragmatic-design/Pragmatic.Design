using System.Text.Json;
using Pragmatic.Messaging.Entities;

namespace PragmaticBoundaries.Tests;

/// <summary>The two events the pump reads back from the outboxes, by the full type name the capture stored.</summary>
public sealed class OutboxEventTypes : IMessageTypeRegistry
{
    // The capture serializes through PragmaticJsonOptions (camelCase).
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public object? Deserialize(string fullyQualifiedTypeName, string json) => fullyQualifiedTypeName switch
    {
        _ when fullyQualifiedTypeName == typeof(OrderPlaced).FullName => JsonSerializer.Deserialize<OrderPlaced>(json, Options),
        _ when fullyQualifiedTypeName == typeof(ShipmentSent).FullName => JsonSerializer.Deserialize<ShipmentSent>(json, Options),
        _ => null,
    };
}
