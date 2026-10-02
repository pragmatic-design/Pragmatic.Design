using Pragmatic.Validation.Types;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Base class for all validation attributes.
/// </summary>
/// <remarks>
///     <para>
///         Validation attributes are processed by the source generator at compile-time
///         to generate efficient validation code. No reflection is used at runtime.
///     </para>
///     <para>
///         Each attribute defines:
///         <list type="bullet">
///             <item><see cref="DefaultMessageKey" /> - The localization key pattern for error messages</item>
///             <item><see cref="IsValid(object?)" /> - The validation logic</item>
///         </list>
///     </para>
///     <para>
///         Message keys follow the pattern: <c>validation.{attribute}.{property}</c>
///         For example: <c>validation.required.email</c>, <c>validation.minlength.name</c>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Custom validation attribute
/// public sealed class FiscalCodeAttribute : ValidationAttribute
/// {
///     public override string DefaultMessageKey => "validation.fiscalcode";
/// 
///     public override bool IsValid(object? value)
///     {
///         if (value is not string s) return true;  // null handled by [Required]
///         return FiscalCodeValidator.IsValid(s);
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = true)]
public abstract class ValidationAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the localization key for the error message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If not set, <see cref="DefaultMessageKey" /> is used.
    ///         The key is resolved by Pragmatic.Localization at the serialization boundary.
    ///     </para>
    ///     <para>
    ///         Example keys:
    ///         <list type="bullet">
    ///             <item>
    ///                 <c>validation.required</c>
    ///             </item>
    ///             <item>
    ///                 <c>validation.email.invalid</c>
    ///             </item>
    ///             <item>
    ///                 <c>custom.myapp.validation.special</c>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public string? MessageKey { get; set; }

    /// <summary>
    ///     Gets the default localization key for this attribute type.
    /// </summary>
    /// <remarks>
    ///     Subclasses must provide a default key. Convention: <c>validation.{attributename}</c>
    /// </remarks>
    public abstract string DefaultMessageKey { get; }

    /// <summary>
    ///     Gets the effective message key (custom or default).
    /// </summary>
    public string EffectiveMessageKey => MessageKey ?? DefaultMessageKey;

    /// <summary>
    ///     Gets or sets the severity level for this validation rule.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see cref="ValidationSeverity.Error" />. Set to <see cref="ValidationSeverity.Warning" />
    ///     for non-blocking warnings or <see cref="ValidationSeverity.Info" /> for informational messages.
    /// </remarks>
    /// <example>
    ///     <code>
    /// [MinLength(8, Severity = ValidationSeverity.Warning)]
    /// public string Password { get; init; }
    /// </code>
    /// </example>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;

    /// <summary>
    ///     Gets or sets the validation groups this rule belongs to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When groups are specified, the rule only executes when the matching group is requested.
    ///         When no groups are specified (null), the rule always executes regardless of the requested group.
    ///     </para>
    ///     <para>
    ///         The generated <c>Validate()</c> method accepts an optional <c>group</c> parameter.
    ///         When a group is specified, only rules matching that group (or rules without groups) run.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// [Required(Groups = ["Create"])]
    /// public string Name { get; init; }
    ///
    /// [Required(Groups = ["Create", "Update"])]
    /// public string Email { get; init; }
    /// </code>
    /// </example>
    public string[]? Groups { get; set; }

    /// <summary>
    ///     Gets whether this attribute requires access to the parent instance for validation.
    /// </summary>
    /// <remarks>
    ///     When <c>true</c>, the source generator will use <see cref="IsValid(object?, object)" />
    ///     instead of <see cref="IsValid(object?)" />.
    /// </remarks>
    public virtual bool RequiresInstance => false;

    /// <summary>
    ///     Validates the value.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns><c>true</c> if valid; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     <para>
    ///         This method is called by the generated validation code.
    ///         Return <c>true</c> for null values unless validating presence (like <c>[Required]</c>).
    ///     </para>
    /// </remarks>
    public abstract bool IsValid(object? value);

    /// <summary>
    ///     Validates the value with access to the parent object for cross-field validation.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="instance">The parent object containing this property.</param>
    /// <returns><c>true</c> if valid; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     Override this method for cross-field validation (e.g., <c>[EqualTo]</c>, <c>[RequiredIf]</c>).
    ///     Default implementation calls <see cref="IsValid(object?)" />.
    /// </remarks>
    public virtual bool IsValid(object? value, object instance)
    {
        return IsValid(value);
    }
}