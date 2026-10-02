using System.Collections.Concurrent;
using Pragmatic.Email.Transport;

namespace Pragmatic.Email.Testing;

/// <summary>
///     In-memory transport that records all sent emails for assertions.
/// </summary>
public sealed class InMemoryTransport : IEmailTransport
{
    private readonly ConcurrentBag<SentEmail> _sent = [];

    public string Name => "InMemory";

    /// <summary>All emails sent so far.</summary>
    public IReadOnlyList<SentEmail> Sent => [.. _sent];

    /// <summary>
    ///     Returns true if any email was sent to the given address.
    ///     Comparison is <see cref="StringComparison.Ordinal"/> (case-sensitive,
    ///     byte-for-byte). RFC 5321 §2.3.11 specifies the local-part as
    ///     case-sensitive while most real-world servers treat addresses
    ///     case-insensitively — test code that exercises both regimes must
    ///     normalize the address itself before calling.
    /// </summary>
    public bool HasSentTo(string address)
        => _sent.Any(e => e.Message.To.Any(a => string.Equals(a.Address, address, StringComparison.Ordinal)));

    /// <summary>Returns true if any email was sent with the given subject.</summary>
    public bool HasSentWithSubject(string subject)
        => _sent.Any(e => e.Message.Subject == subject);

    /// <summary>Returns true if any email matches the predicate.</summary>
    public bool HasSent(Func<EmailMessage, bool> predicate)
        => _sent.Any(e => predicate(e.Message));

    /// <summary>Returns all emails matching the predicate.</summary>
    public IReadOnlyList<SentEmail> SentWhere(Func<EmailMessage, bool> predicate)
        => _sent.Where(e => predicate(e.Message)).ToList();

    /// <summary>Clears all recorded emails.</summary>
    public void Reset() => _sent.Clear();

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Add(new SentEmail(message, DateTimeOffset.UtcNow));
        return Task.FromResult(EmailResult.Succeeded(message.MessageId));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
