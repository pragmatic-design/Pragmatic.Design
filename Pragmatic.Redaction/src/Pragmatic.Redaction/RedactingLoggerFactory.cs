using Microsoft.Extensions.Logging;

namespace Pragmatic.Redaction;

/// <summary>
///     Hands out loggers that mask the members marked <c>[NotLogged]</c> or <c>[PersonalData]</c>
///     before anything downstream sees them.
/// </summary>
/// <remarks>
///     <para>
///         The factory, not the providers. Wrapping providers looked equivalent and is not: a decorator
///         can only wrap the registrations present when it runs, and a test host — or any code calling
///         <c>ConfigureLogging</c> after the application has finished wiring — adds its provider later.
///         Measured on Shunpo, whose <c>WebApplicationFactory</c> adds its log collector after the
///         generated host has already configured everything: provider decoration would have missed
///         exactly the provider the test reads, and the test would have reported the feature broken.
///     </para>
///     <para>
///         One wrap at the factory covers every provider, including ones added afterwards, because each
///         <see cref="ILogger" /> is created through here and fans out to the providers itself.
///     </para>
/// </remarks>
public sealed class RedactingLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _inner;
    private readonly DeclaredRedactor _redactor;

    /// <summary>Wraps <paramref name="inner" /> so the loggers it makes redact declared members.</summary>
    /// <param name="inner">The factory being wrapped.</param>
    /// <param name="redactor">The declared-member redactor.</param>
    public RedactingLoggerFactory(ILoggerFactory inner, DeclaredRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(redactor);

        _inner = inner;
        _redactor = redactor;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        var logger = _inner.CreateLogger(categoryName);

        // Nothing declared anywhere in the application: hand back the original rather than pay a
        // wrapper on every call.
        return _redactor.IsEmpty ? logger : new RedactingLogger(logger, _redactor);
    }

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
