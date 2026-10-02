namespace Pragmatic.Result;

/// <summary>
///     Base class for all error types in the Result pattern.
/// </summary>
/// <remarks>
///     <para>
///         All errors extend this base record which provides common functionality for:
///         <list type="bullet">
///             <item>HTTP status code mapping</item>
///             <item>Problem Details (RFC 7807) support</item>
///             <item>Localization via message keys</item>
///             <item>Transient error detection for retry logic</item>
///         </list>
///     </para>
///     <para>
///         Errors are designed for localization at the serialization boundary.
///         The <see cref="MessageKey" /> property contains a key for looking up localized messages.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public sealed record MyCustomError : Error
/// {
///     public override string Code => "MY_CUSTOM_ERROR";
///     public override int StatusCode => 400;
/// 
///     public string Details { get; init; }
/// }
/// </code>
/// </example>
public abstract record Error : IError
{
    // Per-CODE cache (NOT an instance field): a mutable instance field participates in a record's
    // synthesized Equals/GetHashCode, so populating it on first MessageKey access would change the
    // error's hash and break any HashSet/Dictionary containing it (and make two otherwise-equal errors
    // compare unequal). MessageKey is a pure function of Code, so caching by Code is correct and
    // equality-safe — and unlike a per-Type cache it stays correct for error types whose Code varies
    // per instance (e.g. a generic error parameterized by code). Distinct codes are a finite vocabulary.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> MessageKeyByCode = new(StringComparer.Ordinal);

    /// <summary>
    ///     Message key for localization lookup.
    /// </summary>
    /// <remarks>
    ///     Default implementation converts Code to lowercase with dots.
    ///     Override to provide a custom message key.
    ///     The result is cached per Code after first computation.
    /// </remarks>
    public virtual string MessageKey
        => MessageKeyByCode.GetOrAdd(
            Code,
            static code => $"error.{code.ToLowerInvariant().Replace('_', '.')}");

    /// <summary>
    ///     Parameters for message interpolation during localization.
    /// </summary>
    /// <remarks>
    ///     These parameters are passed to the localizer for string formatting.
    ///     Override to provide context-specific values.
    /// </remarks>
    public virtual IReadOnlyDictionary<string, object>? Parameters { get; init; }

    /// <summary>
    ///     Indicates whether this is a transient error that may succeed on retry.
    /// </summary>
    /// <remarks>
    ///     Transient errors include: timeouts, temporary unavailability, deadlocks.
    ///     Middleware can use this to automatically retry failed operations.
    /// </remarks>
    public virtual bool IsTransient => false;

    /// <summary>
    ///     Suggested delay before retrying a transient operation.
    /// </summary>
    /// <remarks>
    ///     Only meaningful when <see cref="IsTransient" /> is true.
    ///     Null means use default retry delay.
    /// </remarks>
    public virtual TimeSpan? RetryAfter { get; init; }

    /// <summary>
    ///     Semantic error code used for identification and localization lookup.
    /// </summary>
    /// <remarks>
    ///     Convention: UPPER_SNAKE_CASE (e.g., "NOT_FOUND", "VALIDATION_ERROR").
    /// </remarks>
    public abstract string Code { get; }

    /// <summary>
    ///     HTTP status code for this error.
    /// </summary>
    /// <remarks>
    ///     Default mapping:
    ///     <list type="bullet">
    ///         <item>400 - Validation errors (bad request format)</item>
    ///         <item>401 - Authentication errors</item>
    ///         <item>403 - Authorization errors</item>
    ///         <item>404 - Not found errors</item>
    ///         <item>409 - Conflict/domain errors</item>
    ///         <item>422 - Business rule errors (can't process)</item>
    ///         <item>500 - Internal/exception errors</item>
    ///         <item>502/503/504 - Dependency errors</item>
    ///     </list>
    /// </remarks>
    public abstract int StatusCode { get; }

    /// <summary>
    ///     Title for Problem Details response (RFC 7807).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This property is intentionally non-nullable and returns a NON-NULL string. It defaults to
    ///         <c>string.Empty</c> when a derived error does not override it — it never returns <see langword="null"/>.
    ///     </para>
    ///     <para>
    ///         Consumers building a ProblemDetails response MUST treat an empty value as "no specific title"
    ///         and fall back to a generic title (for example one derived from <see cref="StatusCode"/>).
    ///         Do NOT expect or null-check for <see langword="null"/>; check for emptiness instead
    ///         (e.g. <c>string.IsNullOrEmpty(error.Title)</c>).
    ///     </para>
    /// </remarks>
    public virtual string Title => string.Empty;

    /// <summary>
    ///     Additional context about this error. <see langword="null" /> unless a derived error
    ///     supplies one.
    /// </summary>
    /// <remarks>
    ///     Declared here on purpose. <see cref="IError.Description" /> is a default interface member,
    ///     and a type that lists <c>IError</c> without declaring the member binds the interface slot
    ///     to that default for its whole hierarchy — so without this declaration every error deriving
    ///     from this one would answer <see langword="null" /> no matter what it declared, and
    ///     ProblemDetails.Detail would be null for the entire catalogue. Overriding here gives derived
    ///     records a slot they can actually fill.
    /// </remarks>
    public virtual string? Description => null;

    /// <summary>
    ///     Writes custom (non-base) properties as ProblemDetails extensions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The source generator overrides this method for each error type,
    ///         emitting each custom property directly — zero reflection at runtime.
    ///     </para>
    ///     <para>
    ///         The default implementation does nothing. Errors without custom properties
    ///         (or without SG-generated overrides) simply produce no extensions.
    ///     </para>
    /// </remarks>
    /// <param name="extensions">The extensions dictionary to populate.</param>
    public virtual void WriteExtensions(IDictionary<string, object?> extensions) { }
}