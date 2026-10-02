namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     OpenTelemetry standard error and exception tag names.
/// </summary>
/// <remarks>
///     These follow the OTel semantic conventions for exceptions.
///     See: https://opentelemetry.io/docs/specs/semconv/exceptions/exceptions-spans/
/// </remarks>
public static class ErrorTags
{
    /// <summary>
    ///     The error kind on a SPAN — a code, a class of failure. This is the span attribute
    ///     <c>error.type</c>, not the exception-event key.
    /// </summary>
    public const string Type = "error.type";

    /// <summary>
    ///     The fully-qualified exception type name, INSIDE an <c>exception</c> event.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="Type"/> on purpose: the OTel exception event carries
    ///     <c>exception.type</c>, while <c>error.type</c> is a span attribute for the kind of
    ///     failure. One constant serving both would send the event out with <c>error.type</c>, and an
    ///     OTel-aware backend reading the event would find no type at all.
    /// </remarks>
    public const string ExceptionType = "exception.type";

    /// <summary>The exception message.</summary>
    public const string Message = "exception.message";

    /// <summary>The full exception stack trace as a string.</summary>
    public const string Stacktrace = "exception.stacktrace";
}
