using Microsoft.Extensions.Logging;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Default notifier that does NOT deliver the verification token. It logs a warning so a
///     misconfigured app fails loud (verification cannot complete) rather than silently leaking tokens.
///     The message contains neither the email nor the token. Replace with an email-backed
///     <see cref="IEmailVerificationNotifier" /> in production.
/// </summary>
[Service(Lifetime = Lifetime.Singleton)]
public sealed partial class LogOnlyEmailVerificationNotifier(ILogger<LogOnlyEmailVerificationNotifier> logger)
    : IEmailVerificationNotifier
{
    /// <inheritdoc />
    public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        LogNotDelivered();
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Email verification token generated but NOT delivered: no IEmailVerificationNotifier is " +
                  "configured. Register an email-backed notifier to deliver verification tokens to users.")]
    partial void LogNotDelivered();
}
