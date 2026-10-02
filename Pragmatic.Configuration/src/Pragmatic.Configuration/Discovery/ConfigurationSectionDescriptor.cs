namespace Pragmatic.Configuration.Discovery;

/// <summary>
///     One <c>[Configuration]</c> type, as declared: where it binds and what it accepts.
/// </summary>
public sealed class ConfigurationSectionDescriptor
{
    /// <summary>The configuration section this type binds to, e.g. <c>Billing:Invoicing</c>.</summary>
    public required string SectionPath { get; init; }

    /// <summary>The options type, fully qualified.</summary>
    public required string TypeName { get; init; }

    /// <summary>True when the host refuses to start with this section invalid.</summary>
    public bool ValidateOnStart { get; init; }

    /// <summary>The declared properties.</summary>
    public required IReadOnlyList<ConfigurationPropertyDescriptor> Properties { get; init; }

    /// <summary>The full configuration key of a property in this section.</summary>
    public string KeyOf(ConfigurationPropertyDescriptor property)
        => string.IsNullOrEmpty(SectionPath) ? property.Name : $"{SectionPath}:{property.Name}";
}
