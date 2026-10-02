namespace Pragmatic.Notifications.Email;

/// <summary>
///     SMTP options for the notification email channel.
///     Configures sender identity. Transport settings (host, port, auth, pooling)
///     are managed by <see cref="Pragmatic.Email.Configuration.SmtpTransportOptions"/>.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>Sender email address (From header).</summary>
    public required string SenderAddress { get; set; }

    /// <summary>Sender display name.</summary>
    public string? SenderName { get; set; }
}
