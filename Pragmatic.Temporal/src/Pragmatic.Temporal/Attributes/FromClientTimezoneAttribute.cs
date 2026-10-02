namespace Pragmatic.Temporal.Attributes;

/// <summary>
///     Marker attribute indicating that a datetime input should be interpreted
///     in the client's timezone.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FromClientTimezoneAttribute : Attribute
{
}
