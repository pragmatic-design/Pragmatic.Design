namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies the HTTP status an error type is documented with — and, on an endpoint class, the status
///     of its success response.
/// </summary>
/// <remarks>
///     <para>
///         On an error type the attribute decides what the contract says: the OpenAPI document and the
///         client manifest. It is read first, before the status the type's own <c>StatusCode</c> declares,
///         and it is read on the error's <b>bases</b> too — so a family of errors states its status once.
///     </para>
///     <para>
///         ⚠️ It is the only form that survives an assembly boundary. A base in a referenced project is
///         metadata to the build: a <c>StatusCode</c> property value cannot be read there, an attribute
///         argument can. Where the two disagree — the attribute says 410 and <c>StatusCode</c> answers 404 —
///         the build reports <c>PRAG0537</c>: the response is what the caller receives, so the attribute has
///         to agree with it.
///     </para>
///     <para>
///         Without it, the status an error is documented with is a literal <c>StatusCode</c> declared in
///         this compilation; failing that, the first base named like a framework error — <c>NotFoundError</c>
///         → 404, <c>UnauthorizedError</c> → 401, <c>ForbiddenError</c> → 403, <c>ConflictError</c> → 409,
///         <c>ValidationError</c> and <c>BusinessRuleError</c> → 422, <c>InternalServerError</c> → 500,
///         <c>DependencyError</c> → 502 — and failing that, 400.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [HttpStatus(StatusCodes.Status402PaymentRequired)]
/// public record PaymentRequiredError(string Message) : IError
/// {
///     public string Code => "PAYMENT_REQUIRED";
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class HttpStatusAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HttpStatusAttribute" /> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code. Must be in the valid HTTP range [100, 599].</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="statusCode"/> is outside the valid HTTP range [100, 599].
    /// </exception>
    public HttpStatusAttribute(int statusCode)
    {
        if (statusCode is < 100 or > 599)
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode,
                "HTTP status code must be in the valid range [100, 599].");
        StatusCode = statusCode;
    }

    /// <summary>
    ///     Gets the HTTP status code.
    /// </summary>
    public int StatusCode { get; }
}