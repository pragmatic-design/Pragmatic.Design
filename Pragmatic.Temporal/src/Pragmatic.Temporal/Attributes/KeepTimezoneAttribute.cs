namespace Pragmatic.Temporal.Attributes;

/// <summary>
///     Marker attribute indicating that a datetime's timezone should be kept as-is.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class KeepTimezoneAttribute : Attribute
{
}
