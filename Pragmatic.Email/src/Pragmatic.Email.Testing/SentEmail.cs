namespace Pragmatic.Email.Testing;

/// <summary>
///     Recorded email for test assertions.
/// </summary>
public sealed record SentEmail(EmailMessage Message, DateTimeOffset SentAt);
