using Pragmatic.Identity.Local.Services;

namespace TimeOff.Host.Identity;

/// <summary>
///     Development only: the invitations and password resets an email would carry, written to the log
///     instead, so a developer can activate the accounts they create.
/// </summary>
/// <remarks>
///     Time off sends no email of its own. In production an email-backed
///     <see cref="IPasswordResetNotifier" /> replaces this; without one, the framework's default logs
///     that nothing was delivered and never the token.
/// </remarks>
public sealed partial class DevelopmentMailbox(ILogger<DevelopmentMailbox> logger) : IPasswordResetNotifier
{
    public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        LogDelivered(email, token, expiresAt);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "[development mailbox] To {Email}: choose your password with token {Token} at POST /identity/local/reset-password/confirm, before {ExpiresAt}")]
    private partial void LogDelivered(string email, string token, DateTimeOffset expiresAt);
}
