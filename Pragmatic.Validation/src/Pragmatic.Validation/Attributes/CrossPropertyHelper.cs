namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Shared helper for cross-property value resolution in validation attributes.
/// </summary>
internal static class CrossPropertyHelper
{
    /// <summary>
    ///     Resolves a property value from the instance, requiring IPropertyValueProvider.
    /// </summary>
    /// <param name="instance">The object instance to read from.</param>
    /// <param name="propertyName">The property name to resolve.</param>
    /// <param name="attributeName">The attribute name for the error message.</param>
    /// <returns>The resolved property value.</returns>
    /// <exception cref="InvalidOperationException">When the instance does not implement IPropertyValueProvider.</exception>
    internal static object? GetPropertyValue(object instance, string propertyName, string attributeName)
    {
        if (instance is IPropertyValueProvider provider)
            return provider.GetPropertyValue(propertyName);

        throw new InvalidOperationException(
            $"Cross-property validation for [{attributeName}] requires SG-generated validators. Declare the type as partial, or implement IPropertyValueProvider.");
    }
}
