namespace Pragmatic.Temporal.Attributes;

/// <summary>
///     Marker attribute indicating that a datetime output should be converted
///     to the business timezone.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ToBusinessTimezoneAttribute : Attribute
{
}
