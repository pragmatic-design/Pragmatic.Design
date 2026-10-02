namespace Pragmatic.Configuration.Discovery;

/// <summary>
///     One property of a <c>[Configuration]</c> section, as declared.
/// </summary>
/// <remarks>
///     Schema only. There is deliberately no value here: the catalogue describes what an application
///     <i>can</i> be configured with, and a descriptor that carried values would hand out the contents
///     of every <c>[Sensitive]</c> key to anything that can read the catalogue.
/// </remarks>
public sealed class ConfigurationPropertyDescriptor
{
    /// <summary>The property name, which is also the last segment of its configuration key.</summary>
    public required string Name { get; init; }

    /// <summary>The declared CLR type, fully qualified.</summary>
    public required string TypeName { get; init; }

    /// <summary>True when the application cannot start without a value for it.</summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     True when the value is a secret. The catalogue never carries values; this exists so a reader
    ///     — a management UI, a diff against a deployed environment — knows not to display one it
    ///     obtained elsewhere.
    /// </summary>
    public bool IsSensitive { get; init; }
}
