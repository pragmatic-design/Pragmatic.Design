namespace Pragmatic.Notifications.Sms;

/// <summary>
///     Options for the Twilio-backed SMS channel.
/// </summary>
public sealed class TwilioSmsOptions
{
    /// <summary>Twilio Account SID (the <c>AC…</c> identifier).</summary>
    public required string AccountSid { get; set; }

    /// <summary>
    ///     Twilio auth token, used as the Basic-auth password.
    /// </summary>
    /// <remarks>
    ///     A credential: load it from user secrets, environment variables or a secrets manager, never
    ///     from committed configuration.
    /// </remarks>
    public required string AuthToken { get; set; }

    /// <summary>Sender phone number in E.164 form (e.g. <c>+15551234567</c>), or a messaging service SID.</summary>
    public required string FromNumber { get; set; }

    /// <summary>
    ///     Base address of the Twilio REST API (default: <c>https://api.twilio.com</c>).
    /// </summary>
    /// <remarks>
    ///     Overridable so tests can point at a local stub and so a Twilio-compatible gateway can be used
    ///     without a new channel implementation.
    /// </remarks>
    public Uri BaseAddress { get; set; } = new("https://api.twilio.com");

    /// <summary>Request timeout for a single send (default: 30 seconds).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
