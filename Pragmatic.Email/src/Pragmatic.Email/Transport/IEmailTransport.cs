namespace Pragmatic.Email.Transport;

/// <summary>
///     Raw email transport. Pluggable: SMTP, file, in-memory, null.
/// </summary>
public interface IEmailTransport : IAsyncDisposable
{
    /// <summary>Transport name for diagnostics.</summary>
    string Name { get; }

    /// <summary>Sends the email message via this transport.</summary>
    /// <param name="message">The email message to send.</param>
    /// <param name="ct">
    ///     Token used to abort the send operation. Implementations should observe it
    ///     during network I/O and propagate cancellation as
    ///     <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>The outcome of the transport-level send.</returns>
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
