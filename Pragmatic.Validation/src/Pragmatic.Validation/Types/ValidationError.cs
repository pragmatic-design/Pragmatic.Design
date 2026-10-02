using System.Text;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pragmatic.Result;

namespace Pragmatic.Validation.Types;

/// <summary>
///     Validation error with Result-like semantics: IsSuccess when empty, IsFailure when issues exist.
/// </summary>
/// <remarks>
///     <para>
///         ValidationError is both an error type (IHttpError for 400 Bad Request) and has
///         Result-like semantics. This eliminates the need for a separate ValidationResult type.
///     </para>
///     <para>
///         Use collection expressions for static declaration:
///         <code>
/// ValidationError errors = [
///     new ValidationIssue("validation.required", "Email"),
///     new ValidationIssue("validation.min_length", "Name", ("min", 3))
/// ];
/// </code>
///     </para>
///     <para>
///         Use immutable WithFor() methods for fluent building:
///         <code>
/// var error = ValidationError.Valid
///     .WithFor("Email", "validation.required")
///     .WithFor("Name", "validation.min_length", ("min", 3));
/// </code>
///     </para>
/// </remarks>
[CollectionBuilder(typeof(ValidationError), nameof(Create))]
public readonly struct ValidationError : IError, IReadOnlyCollection<ValidationIssue>, IEquatable<ValidationError>
{
    /// <summary>
    ///     Semantic error code for validation failures.
    /// </summary>
    public const string ErrorCode = "VALIDATION_ERROR";

    private readonly ValidationIssue[]? _issues;

    private ValidationError(ValidationIssue[]? issues)
    {
        _issues = issues;
    }

    // =========================================================================
    // Construction
    // =========================================================================

    /// <summary>
    ///     Gets a valid (empty) validation error.
    /// </summary>
    public static ValidationError Valid => default;

    /// <summary>
    ///     Creates a ValidationError from a span of issues (for collection expressions).
    /// </summary>
    /// <param name="issues">The validation issues.</param>
    /// <returns>A ValidationError containing the issues.</returns>
    public static ValidationError Create(ReadOnlySpan<ValidationIssue> issues)
    {
        return issues.Length == 0 ? Valid : new ValidationError(issues.ToArray());
    }

    /// <summary>
    ///     Creates a ValidationError from a collection of issues.
    /// </summary>
    /// <param name="issues">The validation issues.</param>
    /// <returns>A ValidationError containing the issues.</returns>
    public static ValidationError FromIssues(IEnumerable<ValidationIssue> issues)
    {
        var array = issues as ValidationIssue[] ?? issues.ToArray();
        return array.Length == 0 ? Valid : new ValidationError(array);
    }

    /// <summary>
    ///     Creates a ValidationError with a single issue (property-first syntax).
    /// </summary>
    /// <param name="propertyPath">The path to the property that failed validation.</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A ValidationError containing the single issue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationError For(string propertyPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        return new ValidationError([new ValidationIssue(messageKey, propertyPath, parameters)]);
    }

    // =========================================================================
    // IHttpError
    // =========================================================================

    /// <inheritdoc />
    public string Code => ErrorCode;

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         422, not 400, and the difference is one the caller can act on. 400 says the request was
    ///         not understood — malformed JSON, a string where a number goes, a required field absent
    ///         — and the client that sent it has a bug. 422 says it was understood perfectly and the
    ///         rules refuse it: a date in the past, a name too long, a code that does not exist. That
    ///         one has a message for the person, not for the developer.
    ///     </para>
    ///     <para>
    ///         ⚠️ This was 400, which collapsed the two into one answer and left every client to guess
    ///         which it had. The framework already drew the line elsewhere —
    ///         <c>BadRequestError</c> is 400, <c>BusinessRuleError</c> is 422 — and the validators were
    ///         the ones on the wrong side of it. Binding failures stay 400: they genuinely are the
    ///         first kind.
    ///     </para>
    /// </remarks>
    public int StatusCode => 422;

    /// <inheritdoc />
    public string Title => "Validation Failed";

    /// <summary>
    ///     Projects the issues into the RFC 7807 <c>errors</c> extension: property path → message
    ///     keys, the shape ASP.NET Core's own <c>ValidationProblemDetails</c> uses.
    /// </summary>
    /// <remarks>
    ///     Without this the response was a bare <c>{"code":"VALIDATION_ERROR"}</c>: the client knew
    ///     something was wrong and not what. Issues with no property path are collected under the
    ///     empty key, the same convention ASP.NET Core uses for model-level errors.
    /// </remarks>
    public void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (_issues is null || _issues.Length == 0)
            return;

        var byProperty = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var issue in _issues)
        {
            var key = WireKeyOf(issue);
            if (!byProperty.TryGetValue(key, out var messages))
                byProperty[key] = messages = [];
            messages.Add(issue.MessageKey);
        }

        var errors = new Dictionary<string, string[]>(byProperty.Count, StringComparer.Ordinal);
        foreach (var (key, messages) in byProperty)
            errors[key] = [.. messages];

        extensions["errors"] = errors;
    }

    /// <summary>
    ///     Projects the issues as <see cref="WriteExtensions(IDictionary{string, object?})" /> does, and
    ///     beside the keys their messages in the caller's language: <c>messages</c>, property path →
    ///     messages, aligned with <c>errors</c> one for one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two maps because there are two readers. A client matches on the key, which is the same in
    ///         every language, and shows the message, which is not. Replacing the keys with messages
    ///         would have taken the stable half away from the first to give the second what it lacked.
    ///     </para>
    ///     <para>
    ///         A key the resolver does not know keeps its place as the key, so the n-th message of a
    ///         field is always the n-th key's. A resolver that knows none of them adds no map at all:
    ///         the keys a second time would say nothing new.
    ///     </para>
    /// </remarks>
    public void WriteExtensions(IDictionary<string, object?> extensions, IErrorMessageResolver? resolver)
    {
        WriteExtensions(extensions);
        if (resolver is null || _issues is null || _issues.Length == 0)
            return;

        var byProperty = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var resolvedAny = false;
        foreach (var issue in _issues)
        {
            var key = WireKeyOf(issue);
            if (!byProperty.TryGetValue(key, out var messages))
                byProperty[key] = messages = [];

            var message = resolver.ResolveKey(issue.MessageKey, issue.Parameters);
            resolvedAny |= message is not null;
            messages.Add(message ?? issue.MessageKey);
        }

        if (!resolvedAny)
            return;

        var localized = new Dictionary<string, string[]>(byProperty.Count, StringComparer.Ordinal);
        foreach (var (key, messages) in byProperty)
            localized[key] = [.. messages];

        extensions["messages"] = localized;
    }

    /// <summary>
    ///     The name this issue is published under: the one the caller sent, not the one we declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not the C# property name: the payload uses others. A client generated from the
    ///         published contract looks up <c>notEmpty</c>, or <c>people</c> for a property carrying
    ///         <c>[JsonPropertyName]</c>; keyed by <c>NotEmpty</c> or <c>Rows</c>, the field it should
    ///         highlight stays unmarked, and the user sees "something is wrong" next to nothing.
    ///     </para>
    ///     <para>
    ///         Two sources because there are two things to know, each known in one place. An explicit
    ///         rename is on the property and only the generator sees it, so it travels on the issue.
    ///         The camelCase convention is a serialisation choice and only this side knows it, so it
    ///         is applied here. Neither guesses the other's half.
    ///     </para>
    ///     <para>
    ///         <see cref="ValidationIssue.PropertyPath" /> keeps the C# name for the other reader:
    ///         code in the same process, which renames a property with a refactor and expects the
    ///         error to follow.
    ///     </para>
    /// </remarks>
    private static string WireKeyOf(ValidationIssue issue)
    {
        if (!string.IsNullOrEmpty(issue.WirePath))
            return issue.WirePath!;

        var path = issue.PropertyPath;
        if (string.IsNullOrEmpty(path))
            return "";

        // Segment by segment: a nested path is "Lines[0].Product", and only the names camel-case.
        var result = new StringBuilder(path!.Length);
        var atSegmentStart = true;

        foreach (var ch in path!)
        {
            if (ch is '.' or '[' or ']')
            {
                result.Append(ch);
                atSegmentStart = ch != ']';
                continue;
            }

            result.Append(atSegmentStart ? char.ToLowerInvariant(ch) : ch);
            atSegmentStart = false;
        }

        return result.ToString();
    }

    // =========================================================================
    // Content access
    // =========================================================================

    /// <summary>
    ///     Gets the validation issues.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues ?? [];

    // =========================================================================
    // Result-like semantics
    // =========================================================================

    /// <summary>
    ///     Gets whether validation passed (no issues).
    /// </summary>
    [MemberNotNullWhen(false, nameof(_issues))]
    public bool IsSuccess => _issues is null || _issues.Length == 0;

    /// <summary>
    ///     Gets whether validation failed (has issues).
    /// </summary>
    [MemberNotNullWhen(true, nameof(_issues))]
    public bool IsFailure => _issues is not null && _issues.Length > 0;

    /// <summary>
    ///     Pattern matches on valid/invalid state.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="onValid">Called when validation passed.</param>
    /// <param name="onInvalid">Called with issues when validation failed.</param>
    /// <returns>The result from the matched function.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<TResult> onValid,
        Func<IReadOnlyList<ValidationIssue>, TResult> onInvalid)
    {
        return IsSuccess ? onValid() : onInvalid(_issues);
    }

    /// <summary>
    ///     Pattern matches on valid/invalid state with actions.
    /// </summary>
    /// <param name="onValid">Called when validation passed.</param>
    /// <param name="onInvalid">Called with issues when validation failed.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Match(Action onValid, Action<IReadOnlyList<ValidationIssue>> onInvalid)
    {
        if (IsSuccess)
            onValid();
        else
            onInvalid(_issues);
    }

    // =========================================================================
    // IReadOnlyCollection<ValidationIssue>
    // =========================================================================

    /// <summary>
    ///     Gets the number of validation issues.
    /// </summary>
    public int Count => _issues?.Length ?? 0;

    /// <inheritdoc />
    public IEnumerator<ValidationIssue> GetEnumerator()
    {
        return ((IEnumerable<ValidationIssue>)(_issues ?? [])).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // =========================================================================
    // Builder-like methods (immutable, return new ValidationError)
    // =========================================================================

    /// <summary>
    ///     Returns a new ValidationError with the issue added.
    /// </summary>
    /// <param name="issue">The issue to add.</param>
    /// <returns>A new ValidationError containing this error's issues plus the new issue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationError With(ValidationIssue issue)
    {
        if (_issues is null)
            return new ValidationError([issue]);

        // Avoid O(n²) allocations from repeated spread: copy existing array and append
        var newIssues = new ValidationIssue[_issues.Length + 1];
        _issues.CopyTo(newIssues, 0);
        newIssues[_issues.Length] = issue;
        return new ValidationError(newIssues);
    }

    /// <summary>
    ///     Returns a new ValidationError with the issue added (property-first syntax).
    /// </summary>
    /// <param name="propertyPath">The path to the property that failed validation.</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationError containing this error's issues plus the new issue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationError WithFor(string propertyPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        return With(new ValidationIssue(messageKey, propertyPath, parameters));
    }

    /// <summary>
    ///     Returns a new ValidationError with the issue added, specifying severity.
    /// </summary>
    /// <param name="propertyPath">The path to the property that failed validation.</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="severity">The severity level of this issue.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationError containing this error's issues plus the new issue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationError WithFor(string propertyPath, string messageKey,
        ValidationSeverity severity, params (string Key, object Value)[] parameters)
    {
        return With(new ValidationIssue(messageKey, propertyPath, parameters) { Severity = severity });
    }

    /// <summary>
    ///     Returns a new ValidationError with a nested issue added (e.g., "Items[0].ProductId").
    /// </summary>
    /// <param name="parentPath">The parent property path.</param>
    /// <param name="index">The index in the collection.</param>
    /// <param name="childPath">The child property path.</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationError containing this error's issues plus the new issue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationError WithNested(string parentPath, int index, string childPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        return With(ValidationIssue.ForNested(parentPath, index, childPath, messageKey, parameters));
    }

    /// <summary>
    ///     Returns a new ValidationError with a nested object issue added (e.g., "Address.Street").
    /// </summary>
    /// <param name="parentPath">The parent property path.</param>
    /// <param name="childPath">The child property path.</param>
    /// <param name="messageKey">The localization key for the error message.</param>
    /// <param name="parameters">Optional parameters for message interpolation.</param>
    /// <returns>A new ValidationError containing this error's issues plus the new issue.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValidationError WithNested(string parentPath, string childPath, string messageKey,
        params (string Key, object Value)[] parameters)
    {
        return With(ValidationIssue.ForNested(parentPath, childPath, messageKey, parameters));
    }

    /// <summary>
    ///     Combines this error with another ValidationError.
    /// </summary>
    /// <param name="other">The other error to combine with.</param>
    /// <returns>A new ValidationError containing issues from both.</returns>
    public ValidationError Combine(ValidationError other)
    {
        if (IsSuccess)
            return other;
        if (other.IsSuccess)
            return this;
        return new ValidationError([.. _issues, .. other._issues!]);
    }

    /// <summary>
    ///     Combines this error with issues from an enumerable.
    /// </summary>
    /// <param name="issues">The issues to add.</param>
    /// <returns>A new ValidationError containing all issues.</returns>
    public ValidationError Combine(IEnumerable<ValidationIssue> issues)
    {
        var otherArray = issues as ValidationIssue[] ?? issues.ToArray();
        if (otherArray.Length == 0)
            return this;
        if (IsSuccess)
            return new ValidationError(otherArray);
        return new ValidationError([.. _issues, .. otherArray]);
    }

    // =========================================================================
    // Conversions
    // =========================================================================

    /// <summary>
    ///     Converts to VoidResult explicitly.
    /// </summary>
    /// <returns>A VoidResult representing this validation state.</returns>
    /// <remarks>
    ///     Returns Success if this ValidationError has no issues (IsSuccess),
    ///     or Failure containing this error if it has issues (IsFailure).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<ValidationError> ToResult()
    {
        return IsSuccess
            ? VoidResult<ValidationError>.Success()
            : VoidResult<ValidationError>.Failure(this);
    }

    // =========================================================================
    // Backwards Compatibility
    // =========================================================================



    // =========================================================================
    // Equality
    // =========================================================================

    /// <inheritdoc />
    public bool Equals(ValidationError other)
    {
        if (_issues is null && other._issues is null)
            return true;
        if (_issues is null || other._issues is null)
            return false;
        if (_issues.Length != other._issues.Length)
            return false;

        for (var i = 0; i < _issues.Length; i++)
            if (!_issues[i].Equals(other._issues[i]))
                return false;

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ValidationError other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (_issues is null)
            return 0;

        var hash = new HashCode();
        foreach (var issue in _issues)
            hash.Add(issue);
        return hash.ToHashCode();
    }

    /// <summary>
    ///     Equality operator.
    /// </summary>
    public static bool operator ==(ValidationError left, ValidationError right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Inequality operator.
    /// </summary>
    public static bool operator !=(ValidationError left, ValidationError right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsSuccess)
            return "ValidationError.Valid";

        return $"ValidationError ({_issues.Length} issue{(_issues.Length != 1 ? "s" : "")})";
    }
}
