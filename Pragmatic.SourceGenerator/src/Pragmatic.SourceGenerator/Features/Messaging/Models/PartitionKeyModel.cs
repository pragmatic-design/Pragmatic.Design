using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     Model for a message property decorated with [PartitionKey].
/// </summary>
internal sealed record PartitionKeyModel
{
    /// <summary>FQN of the message type owning the property (with global:: prefix).</summary>
    public required string MessageTypeFqn { get; init; }

    /// <summary>Name of the [PartitionKey] property.</summary>
    public required string PropertyName { get; init; }

    /// <summary>True when the property type is a (nullable) string — no ToString() needed.</summary>
    public bool IsString { get; init; }

    /// <summary>True when the property type is a nullable reference/value type.</summary>
    public bool IsNullable { get; init; }

    /// <summary>Owning assembly name (for the generated namespace).</summary>
    public string AssemblyName { get; init; } = "";

    /// <summary>Equatable location snapshot for diagnostics.</summary>
    public LocationInfo? LocationInfo { get; init; }
}
