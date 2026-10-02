using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Pragmatic.Validation.Types;

/// <summary>
///     A single validation problem with a message key, property path, and optional parameters.
/// </summary>
/// <remarks>
///     <para>
///         ValidationIssue represents a single validation failure. The <see cref="MessageKey" />
///         is a localization key (e.g., "validation.email.required") that can be translated
///         via Pragmatic.Localization.
///     </para>
///     <para>
///         Parameters can be used for message interpolation. For example, a message key
///         "validation.string.min_length" might need a "min" parameter for the minimum length.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Simple issue
/// var issue = new ValidationIssue("validation.required", "Email");
/// 
/// // Issue with interpolation parameters
/// var issue = new ValidationIssue("validation.string.min_length", "Name", ("min", 3));
/// 
/// // Using factory method
/// var issue = ValidationIssue.For("Email", "validation.required");
/// </code>
/// </example>
public readonly record struct ValidationIssue
{
    /// <summary>
    ///     Initializes a new validation issue.
    /// </summary>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="propertyPath">The path to the property that failed validation.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    public ValidationIssue(string messageKey, string? propertyPath = null,
        params (string Key, object Value)[] parameters)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(messageKey);

        MessageKey = messageKey;
        PropertyPath = propertyPath;
        // Avoid allocating FrozenDictionary for the common zero-parameter case
        Parameters = parameters is { Length: > 0 }
            ? parameters.ToFrozenDictionary(p => p.Key, p => p.Value)
            : null;
    }

    /// <summary>
    ///     Initializes a new validation issue with a dictionary of parameters.
    /// </summary>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="propertyPath">The path to the property that failed validation.</param>
    /// <param name="parameters">Parameters for message interpolation.</param>
    public ValidationIssue(string messageKey, string? propertyPath, IReadOnlyDictionary<string, object>? parameters)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(messageKey);

        MessageKey = messageKey;
        PropertyPath = propertyPath;
        Parameters = parameters;
    }

    /// <summary>
    ///     Gets the localization key for the error message.
    /// </summary>
    /// <example>"validation.email.required", "validation.string.min_length"</example>
    public string MessageKey { get; }

    /// <summary>
    ///     Gets the path to the property that failed validation.
    /// </summary>
    /// <remarks>
    ///     Can be a simple property name (e.g., "Email") or a nested path (e.g., "Items[0].ProductId").
    ///     Null for cross-property or object-level validation errors.
    /// </remarks>
    public string? PropertyPath { get; }

    /// <summary>
    ///     The name this property travels under, when it differs from the C# one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Filled by the generator only for a property carrying an explicit
    ///         <c>[JsonPropertyName]</c>, because that is the one thing whoever writes the response
    ///         cannot work out: a naming policy it can apply itself, an explicit rename it cannot
    ///         guess. Null everywhere else, and the writer applies the policy to
    ///         <see cref="PropertyPath" />.
    ///     </para>
    ///     <para>
    ///         ⚠️ Two names because there are two readers. Code in the same process reads
    ///         <see cref="PropertyPath" /> and expects the property it can rename with a refactor;
    ///         whoever integrates over HTTP knows only the name in the payload. Publishing the C#
    ///         name to the second was the defect: a client generated from the contract looked for
    ///         <c>people</c> in the error map and found <c>Rows</c>, so the field it should have
    ///         highlighted stayed unmarked.
    ///     </para>
    /// </remarks>
    public string? WirePath { get; init; }

    /// <summary>
    ///     Gets the severity level of this validation issue.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see cref="ValidationSeverity.Error" />. Use <see cref="ValidationSeverity.Warning" />
    ///     for non-blocking issues and <see cref="ValidationSeverity.Info" /> for informational messages.
    /// </remarks>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    ///     Gets the parameters for message interpolation.
    /// </summary>
    /// <remarks>
    ///     Used for parameterized messages like "Must be at least {min} characters"
    ///     where the parameters dictionary contains {"min": 3}.
    /// </remarks>
    public IReadOnlyDictionary<string, object>? Parameters { get; }

    /// <summary>
    ///     Creates a validation issue for a property.
    /// </summary>
    /// <param name="propertyPath">The path to the property.</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationIssue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationIssue For(string propertyPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        return new ValidationIssue(messageKey, propertyPath, parameters);
    }

    /// <summary>
    ///     Creates a validation issue for a nested property (e.g., "Items[0].ProductId").
    /// </summary>
    /// <param name="parentPath">The parent property path (e.g., "Items").</param>
    /// <param name="index">The index in the collection.</param>
    /// <param name="childPath">The child property path (e.g., "ProductId").</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationIssue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationIssue ForNested(string parentPath, int index, string childPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        return new ValidationIssue(messageKey, $"{parentPath}[{index}].{childPath}", parameters);
    }

    /// <summary>
    ///     Creates a validation issue for a nested object property (e.g., "Address.Street").
    /// </summary>
    /// <param name="parentPath">The parent property path (e.g., "Address").</param>
    /// <param name="childPath">The child property path (e.g., "Street").</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationIssue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationIssue ForNested(string parentPath, string childPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        var fullPath = string.IsNullOrEmpty(childPath) ? parentPath : $"{parentPath}.{childPath}";
        return new ValidationIssue(messageKey, fullPath, parameters);
    }

    /// <summary>
    ///     Creates a validation issue without a property path (object-level validation).
    /// </summary>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationIssue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationIssue ForObject(string messageKey, params (string Key, object Value)[] parameters)
    {
        return new ValidationIssue(messageKey, null, parameters);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (PropertyPath is null)
            return MessageKey;

        return Parameters is null or { Count: 0 }
            ? $"{PropertyPath}: {MessageKey}"
            : $"{PropertyPath}: {MessageKey} (params: {Parameters.Count})";
    }
}