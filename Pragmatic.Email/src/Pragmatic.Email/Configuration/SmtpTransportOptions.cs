namespace Pragmatic.Email.Configuration;

/// <summary>
///     SMTP transport configuration: connection, authentication, and pool settings.
/// </summary>
public sealed class SmtpTransportOptions
{
    /// <summary>SMTP server hostname.</summary>
    public required string Host { get; set; }

    /// <summary>SMTP server port (default: 587 for STARTTLS).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Username for SMTP authentication.</summary>
    public string? Username { get; set; }

    /// <summary>
    ///     Password for SMTP authentication.
    /// </summary>
    /// <remarks>
    ///     Never store this value in <c>appsettings.json</c> committed to source control.
    ///     Use environment variables, user secrets, or a secrets manager.
    /// </remarks>
    public string? Password { get; set; }

    /// <summary>
    ///     OAuth2 bearer token for XOAUTH2 authentication.
    /// </summary>
    /// <remarks>
    ///     Never store this value in <c>appsettings.json</c> committed to source control.
    ///     Obtain tokens at runtime from your OAuth2 provider and inject via configuration.
    /// </remarks>
    public string? OAuth2Token { get; set; }

    /// <summary>Authentication method (default: Auto-detect from credentials).</summary>
    public SmtpAuthMethod AuthMethod { get; set; } = SmtpAuthMethod.Auto;

    /// <summary>Whether the connection must be encrypted (default: true).</summary>
    /// <remarks>
    ///     With <see cref="UseImplicitTls"/> off, TLS is negotiated with STARTTLS after the greeting.
    ///     If encryption cannot be established the connection is refused rather than continuing in
    ///     cleartext.
    /// </remarks>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    ///     Whether to start TLS immediately on connect (SMTPS) instead of negotiating STARTTLS.
    ///     <c>null</c> (default) means "decide from the port": implicit TLS on 465, STARTTLS elsewhere.
    /// </summary>
    /// <remarks>
    ///     Providers that expose port 465 expect the session to be encrypted from the first byte and
    ///     never advertise STARTTLS; pointing a STARTTLS client at them simply hangs or fails.
    /// </remarks>
    public bool? UseImplicitTls { get; set; }

    /// <summary>
    ///     Timeout in seconds applied to connect and to every subsequent read and write (default: 30).
    ///     Set to 0 or less to disable.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum concurrent SMTP connections in the pool (default: 5).</summary>
    public int MaxConnections { get; set; } = 5;

    /// <summary>Max messages per connection before recycling (default: 100).</summary>
    public int MaxMessagesPerConnection { get; set; } = 100;

    /// <summary>Idle timeout in seconds before connection is closed (default: 30).</summary>
    public int IdleTimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Hostname sent in the EHLO command (default: <see cref="Environment.MachineName"/>).
    ///     Set to a stable, non-identifying name (e.g. your MTA's public hostname) to avoid
    ///     leaking internal machine identity in SMTP protocol traces.
    /// </summary>
    public string? EhloHostname { get; set; }
}
