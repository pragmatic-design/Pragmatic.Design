namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a value is not null or empty.
/// </summary>
/// <remarks>
///     <para>
///         For strings, validates that the value is not null or empty string.
///         For collections, validates that the value is not null (use <see cref="NotEmptyAttribute" /> for non-empty).
///         For value types, always passes (they cannot be null).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateUserRequest
/// {
///     [Required]
///     public string Email { get; init; }
/// 
///     [Required(MessageKey = "custom.name.required")]
///     public string Name { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RequiredAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.required";

    /// <summary>
    ///     Gets or sets whether to allow empty strings as valid.
    ///     Default is <c>false</c>: empty strings are treated as invalid (same as null).
    /// </summary>
    public bool AllowEmptyStrings { get; set; }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => false,
            string s when !AllowEmptyStrings => !string.IsNullOrEmpty(s),
            string => true,
            _ => true
        };
    }
}