namespace Pragmatic.Configuration;

/// <summary>
/// Marks a class as a configuration options type.
/// The source generator produces: IOptions binding, DataAnnotation validation, and DI registration.
/// </summary>
/// <remarks>
/// The class must be declared as <c>partial</c>.
/// If <see cref="SectionPath"/> is not specified, it is inferred from the class name
/// by removing the "Options" suffix (e.g., <c>BookingOptions</c> → <c>"Booking"</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConfigurationAttribute : Attribute
{
    /// <summary>
    /// Path in the configuration hierarchy (e.g., <c>"Services:OrderApi"</c>).
    /// If omitted, inferred from the class name without the "Options" suffix.
    /// </summary>
    public string? SectionPath { get; init; }

    /// <summary>
    /// When <c>true</c> (default), generates <c>ValidateOnStart()</c> registration
    /// so invalid configuration causes an immediate startup failure.
    /// </summary>
    public bool ValidateOnStart { get; init; } = true;
}
