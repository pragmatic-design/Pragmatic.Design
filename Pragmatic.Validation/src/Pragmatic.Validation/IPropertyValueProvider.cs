namespace Pragmatic.Validation;

/// <summary>
///     Interface for providing property values without reflection.
/// </summary>
/// <remarks>
///     <para>
///         Cross-property validation attributes (e.g., <c>[EqualTo]</c>, <c>[RequiredIf]</c>) need to read
///         another property's value from the instance. The source generator emits direct property access at
///         compile time, so this interface is NOT used in the SG path.
///     </para>
///     <para>
///         This interface exists for scenarios where the SG is not available (e.g., non-partial types,
///         unit testing attribute logic directly). Implementing this interface allows <c>IsValid(value, instance)</c>
///         to resolve the other property value without reflection.
///     </para>
/// </remarks>
public interface IPropertyValueProvider
{
    /// <summary>
    ///     Gets the value of the specified property.
    /// </summary>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The property value, or <c>null</c> if not found.</returns>
    object? GetPropertyValue(string propertyName);
}
