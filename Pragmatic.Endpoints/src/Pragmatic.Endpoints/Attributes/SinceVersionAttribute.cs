namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Marks a property as introduced in a specific API version.
///     Used with convention-based versioning (ExecuteV2, ExecuteV3, etc.)
///     to generate versioned body DTOs that include only properties
///     available in each version.
/// </summary>
/// <remarks>
///     <para>
///         Properties without this attribute are considered part of version 1.0.
///         The version string format is "major.minor" (e.g., "2.0", "3.1").
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [Endpoint(HttpVerb.Post, "/api/orders")]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt;
/// {
///     public required string Name { get; init; }         // Available in all versions
///
///     [SinceVersion("2.0")]
///     public string? Phone { get; init; }                // Available from V2 onwards
///
///     [SinceVersion("3.0")]
///     public string? Email { get; init; }                // Available from V3 onwards
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SinceVersionAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance with the specified version string.
    /// </summary>
    /// <param name="version">The version string in "major.minor" format (e.g., "2.0").</param>
    public SinceVersionAttribute(string version) => Version = version;

    /// <summary>
    ///     Gets the version string when this property was introduced.
    /// </summary>
    public string Version { get; }
}
