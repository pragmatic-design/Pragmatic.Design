namespace Pragmatic.SourceGenerator.Features.Configuration.Models;

/// <summary>One property the generated <c>ITenantOptionsBinder&lt;T&gt;</c> sets from a raw configuration value.</summary>
internal sealed record TypedBindingModel
{
    /// <summary>The property to set.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The configuration key: the property name, or its <c>[ConfigurationKeyName]</c>.</summary>
    public required string Key { get; init; }

    /// <summary>
    ///     The <c>ConfigurationValue</c> method that converts the raw value (<c>Int32</c>,
    ///     <c>Enum&lt;global::X&gt;</c>, …), or null for a string, which is assigned as it is.
    /// </summary>
    public string? Converter { get; init; }

    /// <summary>Whether the property is a <c>Nullable&lt;T&gt;</c>, which an empty value sets to null.</summary>
    public bool IsNullable { get; init; }
}
