namespace Pragmatic.Result.Http;

/// <summary>
///     Represents a business rule violation that prevents processing.
///     Maps to HTTP 422 Unprocessable Entity.
/// </summary>
/// <remarks>
///     <para>
///         Use this when the request is syntactically correct but violates
///         business rules that prevent processing.
///     </para>
///     <para>
///         <b>Difference from 409 Conflict:</b>
///         <list type="bullet">
///             <item>409 - Conflict with current state (retry might work if state changes)</item>
///             <item>422 - Business rule violation (retry won't work with same data)</item>
///         </list>
///     </para>
///     <para>
///         <b>Examples:</b>
///         <list type="bullet">
///             <item>"Insufficient funds" - Transfer amount exceeds balance</item>
///             <item>"Order limit exceeded" - Can't order more than 10 items</item>
///             <item>"Account inactive" - Can't perform action on inactive account</item>
///         </list>
///     </para>
/// </remarks>
public record BusinessRuleError : Error
{
    /// <summary>
    ///     The one-off shape: <see cref="Create(string, string?)" /> fills <see cref="Rule" />.
    /// </summary>
    public BusinessRuleError()
    {
    }

    /// <summary>
    ///     The shape a declared rule uses: the derived type names its rule once, here.
    /// </summary>
    /// <param name="rule">The rule identifier, which also becomes <see cref="Error.MessageKey" />.</param>
    /// <remarks>
    ///     <para>
    ///         Unsealed on purpose. A rule raised from three places as
    ///         <c>Create("worksite-closed", …)</c> repeats the string three times and stays one type,
    ///         so a caller cannot tell it from any other 422 and the endpoint contract cannot name it.
    ///         Deriving keeps the family — the code, the 422, the <c>rule</c> field on the wire, and
    ///         <c>is BusinessRuleError</c> — while giving each rule a type of its own.
    ///     </para>
    ///     <para>
    ///         The parameter is not a property to set but an argument to pass, so a derived rule
    ///         cannot be declared without naming itself.
    ///     </para>
    /// </remarks>
    protected BusinessRuleError(string rule)
    {
        ArgumentException.ThrowIfNullOrEmpty(rule);
        Rule = rule;
    }

    /// <inheritdoc />
    public override string Code => "BUSINESS_RULE_VIOLATION";

    /// <inheritdoc />
    public override int StatusCode => 422;

    /// <inheritdoc />
    public override string Title => "Unprocessable Entity";

    /// <summary>
    ///     Gets the name of the business rule that was violated.
    /// </summary>
    public string? Rule { get; init; }

    /// <summary>
    ///     Gets additional details about the violation.
    /// </summary>
    public string? Details { get; init; }

    /// <inheritdoc />
    public override string MessageKey => Rule is not null
        ? $"error.business_rule.{SanitizeKeySegment(Rule)}"
        : base.MessageKey;

    /// <summary>
    ///     Sanitizes a user-supplied rule name into a safe localization-key segment: lower-cased, with
    ///     every character outside <c>[a-z0-9_.]</c> collapsed to <c>'_'</c>. This prevents path
    ///     separators, null bytes, whitespace, or other special characters in <see cref="Rule"/> from
    ///     producing unexpected key lookups or log-injection when the key is logged.
    /// </summary>
    private static string SanitizeKeySegment(string rule)
    {
        var chars = new char[rule.Length];
        for (var i = 0; i < rule.Length; i++)
        {
            var c = char.ToLowerInvariant(rule[i]);
            chars[i] = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' ? c : '_';
        }
        return new string(chars);
    }

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object>? Parameters { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Rule is not null) extensions["rule"] = Rule;
        if (Details is not null) extensions["details"] = Details;
    }

    /// <summary>
    ///     Creates a BusinessRuleError with the specified rule name.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rule"/> is null or empty.</exception>
    public static BusinessRuleError Create(string rule, string? details = null)
    {
        // Rule is the identifier that drives MessageKey; a null/empty rule would silently fall back to
        // base.MessageKey, masking a programming error. Reject it at the factory.
        ArgumentException.ThrowIfNullOrEmpty(rule);
        return new BusinessRuleError { Rule = rule, Details = details };
    }

    /// <summary>
    ///     Creates a BusinessRuleError with parameters for localization.
    /// </summary>
    public static BusinessRuleError Create(
        string rule,
        IReadOnlyDictionary<string, object> parameters,
        string? details = null)
    {
        return new BusinessRuleError { Rule = rule, Parameters = parameters, Details = details };
    }

    /// <summary>
    ///     Creates a BusinessRuleError for insufficient funds.
    /// </summary>
    public static BusinessRuleError InsufficientFunds(decimal requested, decimal available)
    {
        return new BusinessRuleError
        {
            Rule = "InsufficientFunds",
            Parameters = new Dictionary<string, object>
            {
                ["requested"] = requested,
                ["available"] = available
            }
        };
    }

    /// <summary>
    ///     Creates a BusinessRuleError for exceeding a limit.
    /// </summary>
    public static BusinessRuleError LimitExceeded(string limitType, int limit, int requested)
    {
        return new BusinessRuleError
        {
            Rule = "LimitExceeded",
            Parameters = new Dictionary<string, object>
            {
                ["limitType"] = limitType,
                ["limit"] = limit,
                ["requested"] = requested
            }
        };
    }

    /// <summary>
    ///     Creates a BusinessRuleError for an inactive entity.
    /// </summary>
    public static BusinessRuleError Inactive(string entityType)
    {
        return new BusinessRuleError
        {
            Rule = "Inactive",
            Parameters = new Dictionary<string, object> { ["entityType"] = entityType }
        };
    }
}