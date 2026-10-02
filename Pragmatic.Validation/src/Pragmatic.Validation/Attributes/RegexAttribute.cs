using System.Text.RegularExpressions;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string matches a regular expression pattern.
/// </summary>
/// <remarks>
///     <para>
///         The regex is compiled with a timeout to prevent ReDoS attacks.
///     </para>
///     <para>
///         Null or empty values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateProductRequest
/// {
///     [Regex(@"^[A-Z]{2}-\d{4}$")]  // e.g., "AB-1234"
///     public string ProductCode { get; init; }
/// 
///     [Regex(@"^\d{5}(-\d{4})?$", MessageKey = "validation.zipcode")]
///     public string ZipCode { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class RegexAttribute : ValidationAttribute
{
    private readonly Regex _regex;

    /// <summary>
    ///     Initializes a new instance with the specified pattern.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    public RegexAttribute(string pattern)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(pattern);
        Pattern = pattern;
        _regex = new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    /// <summary>
    ///     Gets the regular expression pattern.
    /// </summary>
    public string Pattern { get; }

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.regex";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string str)
            return true;

        if (string.IsNullOrEmpty(str))
            return true;

        try
        {
            return _regex.IsMatch(str);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}