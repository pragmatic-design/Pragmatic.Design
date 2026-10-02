namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a value is one of a fixed set of allowed values.
/// </summary>
/// <remarks>
///     <para>
///         Uses <see cref="object.Equals(object?, object?)" /> for comparison.
///         Useful for validating string-based status fields, categories, or other constrained values.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record UpdateArticleRequest
/// {
///     [Required]
///     [OneOf("draft", "published", "archived")]
///     public string Status { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OneOfAttribute : ValidationAttribute
{
    /// <summary>
    ///     Initializes a new instance with the set of allowed values.
    /// </summary>
    /// <param name="allowedValues">The allowed values.</param>
    public OneOfAttribute(params object[] allowedValues)
    {
        Ensure.Ensure.ThrowIfNull(allowedValues);
        AllowedValues = allowedValues;
    }

    /// <summary>
    ///     Gets the set of allowed values.
    /// </summary>
    public object[] AllowedValues { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.oneof";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true; // Null check is [Required]'s job

        foreach (var allowed in AllowedValues)
        {
            if (Equals(value, allowed))
                return true;
        }

        return false;
    }
}
