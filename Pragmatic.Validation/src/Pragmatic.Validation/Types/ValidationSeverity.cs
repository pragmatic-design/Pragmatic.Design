namespace Pragmatic.Validation.Types;

/// <summary>
///     Severity level of a validation issue.
/// </summary>
/// <remarks>
///     <para>
///         By default, all validation issues are <see cref="Error" />.
///         Use <see cref="Warning" /> for non-blocking issues (e.g., "password is weak")
///         and <see cref="Info" /> for informational messages (e.g., "field will be normalized").
///     </para>
///     <para>
///         The standard <c>Validate()</c> method collects all severity levels.
///         Filter by severity using LINQ on <see cref="ValidationError.Issues" />:
///         <code>
/// var errors = result.Issues.Where(i => i.Severity == ValidationSeverity.Error);
/// var warnings = result.Issues.Where(i => i.Severity == ValidationSeverity.Warning);
/// </code>
///     </para>
/// </remarks>
public enum ValidationSeverity
{
    /// <summary>
    ///     A blocking validation failure. The operation should not proceed.
    /// </summary>
    Error = 0,

    /// <summary>
    ///     A non-blocking validation warning. The operation can proceed but the user should be informed.
    /// </summary>
    Warning = 1,

    /// <summary>
    ///     An informational validation message. Purely advisory.
    /// </summary>
    Info = 2
}
