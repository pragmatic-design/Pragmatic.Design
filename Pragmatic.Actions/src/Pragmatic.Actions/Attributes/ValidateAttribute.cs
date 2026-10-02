namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Controls validation behavior for a DomainAction.
/// </summary>
/// <remarks>
///     <para>
///         By default, DomainActions execute sync validation (from validation attributes) and, when the
///         compilation declares a <c>[Validator]</c> for the action or for a nested property, the async
///         validators too. This attribute replaces that inference with an explicit choice.
///     </para>
///     <para>
///         <b>Validation modes:</b>
///     </para>
///     <list type="table">
///         <listheader>
///             <term>Attribute</term>
///             <description>Behavior</description>
///         </listheader>
///         <item>
///             <term>(none)</term>
///             <description>Sync validation; async too when a <c>[Validator]</c> for the action (or a nested property) exists (default)</description>
///         </item>
///         <item>
///             <term>[Validate]</term>
///             <description>Sync + Async validation</description>
///         </item>
///         <item>
///             <term>[Validate(Async = true)]</term>
///             <description>Sync + Async validation (explicit)</description>
///         </item>
///         <item>
///             <term>[Validate(AsyncOnly = true)]</term>
///             <description>Async validation only (skip sync)</description>
///         </item>
///         <item>
///             <term>[Validate(Sync = false)]</term>
///             <description>Async validation only (skip sync)</description>
///         </item>
///         <item>
///             <term>[NoValidation]</term>
///             <description>No validation at all</description>
///         </item>
///     </list>
/// </remarks>
/// <example>
///     <code>
/// // Default: sync validation, plus the async validators the compilation declares for it
/// [DomainAction]
/// public partial class CreateUser : DomainAction&lt;UserId&gt;
/// {
///     [Required, Email]
///     public required string Email { get; init; }
/// }
///
/// // Explicit sync + async, whatever the inference would decide
/// [DomainAction]
/// [Validate]  // or [Validate(Async = true)]
/// public partial class CreateUser : DomainAction&lt;UserId&gt;
/// {
///     [Required, Email]
///     public required string Email { get; init; }
/// }
///
/// // Async only (skip sync validation)
/// [DomainAction]
/// [Validate(AsyncOnly = true)]
/// public partial class ImportData : DomainAction&lt;ImportResult&gt;
/// {
///     // Async validator will check data
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ValidateAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets whether to enable async validation.
    ///     Default is <c>true</c> when this attribute is present.
    /// </summary>
    /// <remarks>
    ///     When <c>true</c>, the validation filter will resolve and execute
    ///     <see cref="Pragmatic.Validation.IAsyncValidator{T}" /> if registered.
    /// </remarks>
    public bool Async { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether to enable sync validation.
    ///     Default is <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     When <c>false</c>, skips <see cref="Pragmatic.Validation.ISyncValidator" />
    ///     even if the action implements it.
    /// </remarks>
    public bool Sync { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether to run async validation only (skip sync).
    ///     This is equivalent to setting <c>Sync = false</c>.
    /// </summary>
    /// <remarks>
    ///     Use this when you want to skip attribute-based sync validation
    ///     and only run the async validator.
    /// </remarks>
    public bool AsyncOnly
    {
        get => !Sync && Async;
        set
        {
            if (value)
            {
                Sync = false;
                Async = true;
            }
            else
            {
                // AsyncOnly = false re-enables sync validation (undoes a prior AsyncOnly = true),
                // otherwise the setter would be a silent no-op.
                Sync = true;
            }
        }
    }
}
