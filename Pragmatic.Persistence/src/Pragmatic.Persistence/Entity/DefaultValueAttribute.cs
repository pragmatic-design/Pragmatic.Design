namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Specifies a static default value for an entity property.
///     The SG generates code to apply the default when the property is unset at creation time.
/// </summary>
/// <example>
///     <code>
/// [DefaultValue("EUR")]
/// public string Currency { get; set; }
///
/// [DefaultValue(1)]
/// public int GuestCount { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DefaultValueAttribute(object value) : Attribute
{
    /// <summary>
    ///     The default value to assign.
    /// </summary>
    public object Value { get; } = value;
}
