using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Pragmatic.Email.Configuration;

namespace Pragmatic.Email.Smtp;

/// <summary>
///     Single SMTP connection. Handles TCP, STARTTLS, AUTH, and sending messages.
/// </summary>
internal sealed class SmtpConnection : IAsyncDisposable
{
    private TcpClient? _tcp;
    private Stream? _stream;
    private readonly HashSet<string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private int _messagesSent;
    private DateTimeOffset _lastUsedAt;
    // Stored at connect-time to avoid re-capturing options in SendEhloAsync.
    private string? _ehloHostname;
    // Applied to every network operation. TcpClient.Send/ReceiveTimeout only affect the synchronous
    // socket APIs — ReadAsync/WriteAsync ignore them — so without this a silent server would hang the
    // caller forever whenever no CancellationToken was passed to SendAsync.
    private TimeSpan _ioTimeout = Timeout.InfiniteTimeSpan;

    public bool IsConnected => _tcp?.Connected == true;
    public int MessagesSent => _messagesSent;
    public DateTimeOffset LastUsedAt => _lastUsedAt;
    public bool SupportsPipelining => _capabilities.Contains("PIPELINING");

    public async Task ConnectAsync(SmtpTransportOptions options, CancellationToken ct)
    {
        _ehloHostname = options.EhloHostname is { Length: > 0 } h ? h : Environment.MachineName;
        _ioTimeout = options.TimeoutSeconds > 0
            ? TimeSpan.FromSeconds(options.TimeoutSeconds)
            : Timeout.InfiniteTimeSpan;

        _tcp = new TcpClient();
        _tcp.SendTimeout = options.TimeoutSeconds * 1000;
        _tcp.ReceiveTimeout = options.TimeoutSeconds * 1000;

        using (var connectCts = CreateTimeoutScope(ct))
            await _tcp.ConnectAsync(options.Host, options.Port, connectCts.Token).ConfigureAwait(false);

        _stream = _tcp.GetStream();

        // Implicit TLS (SMTPS): the session is encrypted from the first byte, before the greeting.
        // Providers exposing port 465 require this and never advertise STARTTLS.
        if (options.UseImplicitTls ?? options.Port == ImplicitTlsPort)
        {
            _stream = await AuthenticateTlsAsync(options, ct).ConfigureAwait(false);
        }

        await ReadResponseAsync(ct).ConfigureAwait(false); // 220 greeting

        await SendEhloAsync(ct).ConfigureAwait(false);

        if (options.UseSsl && _stream is not SslStream && _capabilities.Contains("STARTTLS"))
        {
            await SendCommandAsync("STARTTLS", ct).ConfigureAwait(false);
            var tlsResponse = await ReadResponseAsync(ct).ConfigureAwait(false);
            if (tlsResponse.StartsWith("220", StringComparison.Ordinal))
            {
                _stream = await AuthenticateTlsAsync(options, ct).ConfigureAwait(false);
                _capabilities.Clear();
                await SendEhloAsync(ct).ConfigureAwait(false);
            }
        }

        // Fail closed: if the caller asked for a secure connection but TLS was never
        // established (server did not advertise STARTTLS, or the handshake failed),
        // refuse to authenticate — credentials must never travel over a cleartext socket.
        if (options.UseSsl && _stream is not SslStream)
        {
            throw new InvalidOperationException(
                "SMTP connection requires TLS (UseSsl is enabled) but it could not be established: the "
                + "server did not advertise STARTTLS. If the endpoint speaks implicit TLS (usually port "
                + $"{ImplicitTlsPort}), set SmtpTransportOptions.UseImplicitTls. Refusing to continue: "
                + "credentials would be sent unencrypted.");
        }

        await AuthenticateAsync(options, ct).ConfigureAwait(false);
        _lastUsedAt = DateTimeOffset.UtcNow;
    }

    public async Task<EmailResult> SendMessageAsync(string mimeBody, EmailMessage message, CancellationToken ct)
    {
        // MAIL FROM — strip CR/LF as defense-in-depth: an address constructed via the implicit
        // string operator bypasses EmailAddress.Validated, so a raw address could otherwise inject
        // additional SMTP commands here.
        var fromAddress = StripLineBreaks(message.From.Address);
        await SendCommandAsync($"MAIL FROM:<{fromAddress}>", ct).ConfigureAwait(false);
        var fromResponse = await ReadResponseAsync(ct).ConfigureAwait(false);
        if (!fromResponse.StartsWith("250", StringComparison.Ordinal))
            return EmailResult.Failed($"MAIL FROM rejected: {fromResponse}");

        // RCPT TO — all recipients
        foreach (var to in message.To.Concat(message.Cc).Concat(message.Bcc))
        {
            var toAddress = StripLineBreaks(to.Address);
            await SendCommandAsync($"RCPT TO:<{toAddress}>", ct).ConfigureAwait(false);
            var rcptResponse = await ReadResponseAsync(ct).ConfigureAwait(false);
            if (!rcptResponse.StartsWith("250", StringComparison.Ordinal))
                return EmailResult.Failed($"RCPT TO <{toAddress}> rejected: {rcptResponse}");
        }

        // DATA
        await SendCommandAsync("DATA", ct).ConfigureAwait(false);
        var dataResponse = await ReadResponseAsync(ct).ConfigureAwait(false);
        if (!dataResponse.StartsWith("354", StringComparison.Ordinal))
            return EmailResult.Failed($"DATA rejected: {dataResponse}");

        // Send MIME body + terminator. Dot-stuffing is applied here, over the whole DATA block:
        // without it a payload line starting with "." (including the very first one) would close
        // DATA early and the remainder would be executed as SMTP commands on this authenticated
        // session. The signed message is the un-stuffed one — the server strips the stuffing again.
        var payload = SmtpDotStuffing.Apply(mimeBody);
        await SendCommandAsync(payload + "\r\n.", ct).ConfigureAwait(false);
        var sendResponse = await ReadResponseAsync(ct).ConfigureAwait(false);
        if (!sendResponse.StartsWith("250", StringComparison.Ordinal))
            return EmailResult.Failed($"Message rejected: {sendResponse}");

        _messagesSent++;
        _lastUsedAt = DateTimeOffset.UtcNow;
        return EmailResult.Succeeded(message.MessageId);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (_stream is not null)
        {
            try
            {
                await SendCommandAsync("QUIT", ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort quit
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _tcp?.Dispose();
        _tcp = null;
    }

    // Removes CR/LF so a malformed address cannot terminate the current SMTP command and inject
    // a new one. EmailAddress.Validated already rejects these for builder-constructed addresses;
    // this is the transport-level safety net for addresses created via the implicit operator.
    private static string StripLineBreaks(string value) =>
        value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0
            ? value
            : value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    private async Task SendEhloAsync(CancellationToken ct)
    {
        await SendCommandAsync($"EHLO {_ehloHostname}", ct).ConfigureAwait(false);
        var response = await ReadResponseAsync(ct).ConfigureAwait(false);

        foreach (var line in response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 4)
                _capabilities.Add(line[4..].Split(' ')[0]);
        }
    }

    private async Task AuthenticateAsync(SmtpTransportOptions options, CancellationToken ct)
    {
        var method = options.AuthMethod;
        if (method == SmtpAuthMethod.Auto)
        {
            if (options.OAuth2Token is not null)
                method = SmtpAuthMethod.XOAuth2;
            else if (options.Username is not null)
                method = SmtpAuthMethod.Plain;
            else
                return; // No auth
        }

        switch (method)
        {
            case SmtpAuthMethod.Plain:
                await AuthPlainAsync(options.Username!, options.Password ?? "", ct).ConfigureAwait(false);
                break;
            case SmtpAuthMethod.Login:
                await AuthLoginAsync(options.Username!, options.Password ?? "", ct).ConfigureAwait(false);
                break;
            case SmtpAuthMethod.XOAuth2:
                await AuthXOAuth2Async(options.Username!, options.OAuth2Token!, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task AuthPlainAsync(string username, string password, CancellationToken ct)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{username}\0{password}"));
        await SendCommandAsync($"AUTH PLAIN {token}", ct).ConfigureAwait(false);
        var response = await ReadResponseAsync(ct).ConfigureAwait(false);
        if (!response.StartsWith("235", StringComparison.Ordinal))
        {
            // Do not include the raw server response: it may contain partial credentials or tokens.
            var code = response.Length >= 3 ? response[..3] : "???";
            throw new InvalidOperationException($"AUTH PLAIN failed with code {code}.");
        }
    }

    private async Task AuthLoginAsync(string username, string password, CancellationToken ct)
    {
        await SendCommandAsync("AUTH LOGIN", ct).ConfigureAwait(false);
        await ReadResponseAsync(ct).ConfigureAwait(false); // 334 prompt

        await SendCommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(username)), ct).ConfigureAwait(false);
        await ReadResponseAsync(ct).ConfigureAwait(false); // 334 prompt

        await SendCommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(password)), ct).ConfigureAwait(false);
        var response = await ReadResponseAsync(ct).ConfigureAwait(false);
        if (!response.StartsWith("235", StringComparison.Ordinal))
        {
            // Do not include the raw server response: it may contain partial credentials or tokens.
            var code = response.Length >= 3 ? response[..3] : "???";
            throw new InvalidOperationException($"AUTH LOGIN failed with code {code}.");
        }
    }

    private async Task AuthXOAuth2Async(string username, string token, CancellationToken ct)
    {
        var sasl = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"user={username}\x01auth=Bearer {token}\x01\x01"));
        await SendCommandAsync($"AUTH XOAUTH2 {sasl}", ct).ConfigureAwait(false);
        var response = await ReadResponseAsync(ct).ConfigureAwait(false);
        if (!response.StartsWith("235", StringComparison.Ordinal))
        {
            // Do not include the raw server response: it may contain OAuth2 error details with token fragments.
            var code = response.Length >= 3 ? response[..3] : "???";
            throw new InvalidOperationException($"AUTH XOAUTH2 failed with code {code}.");
        }
    }

    /// <summary>The conventional SMTPS port, where TLS starts before the greeting.</summary>
    private const int ImplicitTlsPort = 465;

    /// <summary>Performs the TLS handshake over the current socket and returns the encrypted stream.</summary>
    private async Task<SslStream> AuthenticateTlsAsync(SmtpTransportOptions options, CancellationToken ct)
    {
        var sslStream = new SslStream(_tcp!.GetStream(), leaveInnerStreamOpen: false);

        using var timeout = CreateTimeoutScope(ct);
        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = options.Host }, timeout.Token).ConfigureAwait(false);

        return sslStream;
    }

    /// <summary>
    ///     Links the caller's token with the configured I/O timeout, so a server that accepts the
    ///     connection and then goes silent cannot stall the send indefinitely.
    /// </summary>
    private CancellationTokenSource CreateTimeoutScope(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_ioTimeout != Timeout.InfiniteTimeSpan)
            cts.CancelAfter(_ioTimeout);

        return cts;
    }

    internal async Task SendCommandAsync(string command, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(command + "\r\n");

        using var timeout = CreateTimeoutScope(ct);
        await _stream!.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        await _stream.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    internal async Task<string> ReadResponseAsync(CancellationToken ct)
    {
        var buffer = new byte[2048];
        var sb = new StringBuilder();

        // Guard: max 512 reads to prevent infinite loop on malformed/truncated server responses.
        // A legitimate multi-line greeting/EHLO response rarely exceeds a handful of reads.
        const int MaxReads = 512;
        var reads = 0;

        // One deadline for the whole response, not per read: a server dribbling a byte at a time
        // would otherwise keep resetting a per-read timeout and hold the connection open forever.
        using var timeout = CreateTimeoutScope(ct);

        while (reads < MaxReads)
        {
            reads++;
            var read = await _stream!.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (read == 0) break;

            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var response = sb.ToString();

            var lines = response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && lines[^1].Length >= 4 && lines[^1][3] == ' ')
                return response;
        }

        return sb.ToString();
    }
}
