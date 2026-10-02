namespace Pragmatic.Email.Transport;

/// <summary>
///     No-op transport. Always succeeds without sending anything.
/// </summary>
public sealed class NullTransport : IEmailTransport
{
    public string Name => "Null";

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        => Task.FromResult(EmailResult.Succeeded(message.MessageId));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
