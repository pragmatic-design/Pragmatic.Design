namespace Pragmatic.Validation;

/// <summary>
///     Configuration options for Pragmatic.Validation.
/// </summary>
/// <remarks>
///     <para>
///         Configure validation behavior globally via <c>AddPragmaticValidation()</c>:
///     </para>
///     <code>
/// builder.Services.AddPragmaticValidation(options =>
/// {
///     options.FailFast = true;  // Stop on first error
/// });
/// </code>
/// </remarks>
public sealed class ValidationOptions
{
    /// <summary>
    ///     Gets or sets whether validation should stop at the first error.
    ///     Default is <c>false</c> (accumulate all errors).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When <c>false</c> (default): All validation rules are executed and all errors
    ///         are collected. This provides a better user experience as users see all
    ///         issues at once.
    ///     </para>
    ///     <para>
    ///         When <c>true</c>: Validation stops at the first error. This can improve
    ///         performance in scenarios where fixing one error at a time is acceptable.
    ///     </para>
    ///     <para>
    ///         <b>Note:</b> Even with <c>FailFast = false</c>, sync validation always
    ///         runs before async validation. If sync validation fails and the validated
    ///         type has no async validator, only sync errors are returned.
    ///     </para>
    /// </remarks>
    public bool FailFast { get; set; }

}