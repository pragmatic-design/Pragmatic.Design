namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Accesses properties on an object without reflection.
/// Implementations: <see cref="DictionaryPropertyAccessor"/> (built-in), SG-generated for typed DTOs.
/// </summary>
public interface IPropertyAccessor
{
    /// <summary>Get the value of a named property on the target object.</summary>
    object? GetValue(object target, string propertyName);

    /// <summary>Check if the target has the named property.</summary>
    bool HasProperty(object target, string propertyName);
}
