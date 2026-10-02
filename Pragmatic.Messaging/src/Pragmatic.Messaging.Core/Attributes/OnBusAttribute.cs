using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Specifies which named bus a message handler should consume from.
///     When not specified, handlers consume from the default bus.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[method: SetsRequiredMembers]
public sealed class OnBusAttribute(string busName) : Attribute
{
    /// <summary>The bus name (e.g., "integration", "analytics").</summary>
    public required string BusName { get; init; } = busName;
}
