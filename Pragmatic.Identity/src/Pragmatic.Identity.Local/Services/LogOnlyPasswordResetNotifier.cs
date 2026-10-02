using Microsoft.Extensions.Logging;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Default notifier that does NOT deliver the token. It logs a warning so a misconfigured app
///     fails loud (the reset feature does nothing) rather than silently leaking tokens. The message
///     intentionally contains neither the email nor the token. Replace with an email/SMS-backed
///     <see cref="IPasswordResetNotifier" /> in production.
/// </summary>
[Service(Lifetime = Lifetime.Singleton)]
public sealed partial class LogOnlyPasswordResetNotifier(ILogger<LogOnlyPasswordResetNotifier> logger)
    : IPasswordResetNotifier
{
    /// <inheritdoc />
    public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        LogNotDelivered();
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Password reset token generated but NOT delivered: no IPasswordResetNotifier is configured. " +
                  "Register an email-backed notifier to deliver reset tokens to users.")]
    partial void LogNotDelivered();
}
