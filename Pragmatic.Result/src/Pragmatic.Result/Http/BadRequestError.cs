namespace Pragmatic.Result.Http;

/// <summary>
///     Represents a generic bad request error.
///     Maps to HTTP 400 Bad Request.
/// </summary>
/// <remarks>
///     <para>
///         For validation errors with multiple issues, prefer using
///         <c>ValidationError</c> from Pragmatic.Validation instead.
///     </para>
///     <para>
///         Use this for simple bad request scenarios like malformed JSON,
///         missing required headers, or unsupported locales.
///     </para>
/// </remarks>
public sealed record BadRequestError : Error
{
    /// <inheritdoc />
    public override string Code => "BAD_REQUEST";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Bad Request";

    /// <summary>
    ///     Gets the reason for the bad request.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the field/parameter that caused the error (if applicable).
    /// </summary>
    public string? Field { get; init; }

    /// <inheritdoc />
    public override string MessageKey => Reason switch
    {
        "MalformedJson" => "error.bad_request.malformed_json",
        "MissingHeader" => "error.bad_request.missing_header",
        "UnsupportedLocale" => "error.bad_request.unsupported_locale",
        "InvalidParameter" => "error.bad_request.invalid_parameter",
        _ => base.MessageKey
    };

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object>? Parameters => BuildParameters();

    private IReadOnlyDictionary<string, object>? BuildParameters()
    {
        var dict = new Dictionary<string, object>();
        if (Reason is not null)
            dict["reason"] = Reason;
        if (Field is not null)
            dict["field"] = Field;
        return dict.Count > 0 ? dict : null;
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Reason is not null) extensions["reason"] = Reason;
        if (Field is not null) extensions["field"] = Field;
    }

    /// <summary>
    ///     Creates a BadRequestError with the specified reason.
    /// </summary>
    public static BadRequestError Create(string reason)
    {
        return new BadRequestError { Reason = reason };
    }

    /// <summary>
    ///     Creates a BadRequestError for malformed JSON.
    /// </summary>
    public static BadRequestError MalformedJson()
    {
        return new BadRequestError { Reason = "MalformedJson" };
    }

    /// <summary>
    ///     Creates a BadRequestError for a missing required header.
    /// </summary>
    public static BadRequestError MissingHeader(string headerName)
    {
        return new BadRequestError { Reason = "MissingHeader", Field = headerName };
    }

    /// <summary>
    ///     Creates a BadRequestError for an unsupported locale.
    /// </summary>
    public static BadRequestError UnsupportedLocale(string locale)
    {
        return new BadRequestError { Reason = "UnsupportedLocale", Field = locale };
    }

    /// <summary>
    ///     Creates a BadRequestError for an invalid parameter.
    /// </summary>
    public static BadRequestError InvalidParameter(string parameterName, string reason)
    {
        return new BadRequestError { Field = parameterName, Reason = reason };
    }
}