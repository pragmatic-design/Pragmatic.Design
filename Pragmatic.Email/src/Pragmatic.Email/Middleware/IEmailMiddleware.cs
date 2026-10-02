namespace Pragmatic.Email.Middleware;

/// <summary>
///     Email pipeline step. Transforms the message before it reaches the transport.
///     Examples: DKIM signing, header injection, tracking pixel, branding.
/// </summary>
public interface IEmailMiddleware
{
    /// <summary>Execution order. Lower runs first.</summary>
    int Order => 0;

    /// <summary>
    ///     Processes the message. Call <paramref name="next"/> to continue the pipeline,
    ///     or return a modified message without calling next to short-circuit.
    /// </summary>
    Task<EmailMessage> ProcessAsync(
        EmailMessage message,
        Func<EmailMessage, Task<EmailMessage>> next,
        CancellationToken ct);
}
