namespace Pragmatic.Result.Http;

/// <summary>
///     Represents an authentication error when credentials are missing or invalid.
///     Maps to HTTP 401 Unauthorized.
/// </summary>
/// <remarks>
///     <para>
///         Use this when the user is not authenticated (no token, invalid token, expired token).
///     </para>
///     <para>
///         For authorization failures (authenticated but lacking permission),
///         use <see cref="ForbiddenError" /> instead.
///     </para>
/// </remarks>
public sealed record UnauthorizedError : Error
{
    /// <inheritdoc />
    public override string Code => "UNAUTHORIZED";

    /// <inheritdoc />
    public override int StatusCode => 401;

    /// <inheritdoc />
    public override string Title => "Unauthorized";

    /// <summary>
    ///     Gets the reason for the authentication failure.
    /// </summary>
    public string? Reason { get; init; }

    /// <inheritdoc />
    public override string MessageKey => Reason switch
    {
        "MissingToken" => "error.unauthorized.missing_token",
        "InvalidToken" => "error.unauthorized.invalid_token",
        "ExpiredToken" => "error.unauthorized.expired_token",
        "InvalidCredentials" => "error.unauthorized.invalid_credentials",
        _ => base.MessageKey
    };

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Reason is not null) extensions["reason"] = Reason;
    }

    /// <summary>
    ///     Creates an UnauthorizedError with an optional reason.
    /// </summary>
    public static UnauthorizedError Create(string? reason = null)
    {
        return new UnauthorizedError { Reason = reason };
    }

    /// <summary>
    ///     Creates an UnauthorizedError for missing authentication token.
    /// </summary>
    public static UnauthorizedError MissingToken()
    {
        return new UnauthorizedError { Reason = "MissingToken" };
    }

    /// <summary>
    ///     Creates an UnauthorizedError for an invalid token.
    /// </summary>
    public static UnauthorizedError InvalidToken()
    {
        return new UnauthorizedError { Reason = "InvalidToken" };
    }

    /// <summary>
    ///     Creates an UnauthorizedError for an expired token.
    /// </summary>
    public static UnauthorizedError ExpiredToken()
    {
        return new UnauthorizedError { Reason = "ExpiredToken" };
    }

    /// <summary>
    ///     Creates an UnauthorizedError for invalid credentials.
    /// </summary>
    public static UnauthorizedError InvalidCredentials()
    {
        return new UnauthorizedError { Reason = "InvalidCredentials" };
    }
}