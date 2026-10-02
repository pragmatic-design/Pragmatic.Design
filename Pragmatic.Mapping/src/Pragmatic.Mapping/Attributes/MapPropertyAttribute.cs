namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Customizes property mapping behavior.
///     <para>
///         Can specify: source path(s), format string, default value, and separator for concatenation.
///     </para>
/// </summary>
/// <example>
///     <code>
///     [MapFrom&lt;User&gt;]
///     public partial record UserDto
///     {
///         // 1. Single path (flattening)
///         [MapProperty("Address.City")]
///         public string City { get; init; }
/// 
///         // 2. Multiple paths (concatenation)
///         [MapProperty(nameof(User.FirstName), nameof(User.LastName))]
///         public string FullName { get; init; }
/// 
///         // 3. Custom separator
///         [MapProperty(nameof(User.FirstName), nameof(User.LastName), Separator = ", ")]
///         public string FullNameReversed { get; init; }
/// 
///         // 4. Format string
///         [MapProperty(nameof(User.CreatedAt), Format = "yyyy-MM-dd")]
///         public string CreatedDate { get; init; }
/// 
///         // 5. Default value for nullable
///         [MapProperty(nameof(User.MiddleName), Default = "N/A")]
///         public string MiddleName { get; init; }
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance with no custom source path.
    ///     Used to force ID mapping in <c>[MapTo]</c> scenarios.
    /// </summary>
    public MapPropertyAttribute()
    {
        SourcePaths = [];
    }

    /// <summary>
    ///     Initializes a new instance mapping from a single source property path.
    /// </summary>
    /// <param name="sourcePath">
    ///     The source property path. Supports nested paths like "Address.City".
    /// </param>
    public MapPropertyAttribute(string sourcePath)
    {
        SourcePaths = [sourcePath];
    }

    /// <summary>
    ///     Initializes a new instance mapping from multiple source properties (concatenation).
    /// </summary>
    /// <param name="sourcePaths">
    ///     The source property paths to concatenate. Use <see cref="Separator" /> to customize.
    /// </param>
    /// <remarks>
    ///     When passing a single string literal the compiler resolves to
    ///     <see cref="MapPropertyAttribute(string)" /> rather than this overload.
    ///     This is intentional: single-path callers always hit the non-params constructor.
    ///     Only calls with two or more arguments (or an explicit array) reach this overload.
    /// </remarks>
    public MapPropertyAttribute(params string[] sourcePaths)
    {
        SourcePaths = sourcePaths;
    }

    /// <summary>
    ///     Gets the source property paths for this mapping.
    /// </summary>
    public string[] SourcePaths { get; }

    /// <summary>
    ///     Gets or sets the format string for the source value.
    ///     <para>
    ///         Applied using <c>ToString(format)</c> on the source value.
    ///         Not supported in projections (PRAG0321 warning).
    ///     </para>
    /// </summary>
    /// <example>
    ///     <code>
    ///     [MapProperty(nameof(User.BirthDate), Format = "yyyy-MM-dd")]
    ///     public string BirthDate { get; init; }
    ///     </code>
    /// </example>
    public string? Format { get; set; }

    /// <summary>
    ///     Gets or sets the separator used when concatenating multiple source values.
    ///     Defaults to a single space " ".
    /// </summary>
    /// <example>
    ///     <code>
    ///     [MapProperty("LastName", "FirstName", Separator = ", ")]
    ///     public string FullName { get; init; }  // "Doe, John"
    ///     </code>
    /// </example>
    public string Separator { get; set; } = " ";

    /// <summary>
    ///     Gets or sets the default value when mapping from nullable to non-nullable.
    ///     <para>
    ///         Required when source is nullable and target is non-nullable (PRAG0317 error otherwise).
    ///     </para>
    /// </summary>
    /// <example>
    ///     <code>
    ///     // Source: string? MiddleName
    ///     [MapProperty(nameof(User.MiddleName), Default = "")]
    ///     public string MiddleName { get; init; }
    ///     </code>
    /// </example>
    public object? Default { get; set; }

    /// <summary>
    ///     Gets or sets the target property path for <c>[MapTo]</c> scenarios.
    ///     <para>
    ///         Used when DTO property should map to a nested entity property.
    ///         Supports nested paths like "Customer.Name" or "ShippingAddress.City".
    ///     </para>
    /// </summary>
    /// <example>
    ///     <code>
    ///     [MapTo&lt;Order&gt;]
    ///     public partial record UpdateOrderDto
    ///     {
    ///         // Maps this.CustomerName to entity.Customer.Name
    ///         [MapProperty(Target = "Customer.Name")]
    ///         public string CustomerName { get; init; }
    ///     }
    ///     </code>
    /// </example>
    public string? Target { get; set; }
}