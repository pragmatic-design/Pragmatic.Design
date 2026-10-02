namespace Pragmatic.Temporal.Attributes;

/// <summary>
///     Marker attribute indicating that a datetime should be treated as UTC.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AsUtcAttribute : Attribute
{
}
