namespace Pragmatic.Email;

/// <summary>
///     Immutable email message. Middleware creates copies via <c>with {}</c>.
/// </summary>
public sealed record EmailMessage
{
    /// <summary>
    ///     Sender address. May be left unset, in which case
    ///     <see cref="Configuration.EmailOptions.DefaultFrom"/> is applied before the message reaches
    ///     the transport; sending with neither fails with an explanatory error.
    /// </summary>
    public EmailAddress From { get; init; }
    public required IReadOnlyList<EmailAddress> To { get; init; }
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public EmailAddress? ReplyTo { get; init; }
    public required string Subject { get; init; }
    public string? TextBody { get; init; }
    public string? HtmlBody { get; init; }
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public string MessageId { get; init; } = $"{Guid.CreateVersion7():N}@pragmatic.email";
    public DateTimeOffset Date { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     A pre-rendered content block (content headers, blank line, body) that replaces the body the
    ///     MIME writer would otherwise build from <see cref="TextBody"/>, <see cref="HtmlBody"/> and
    ///     <see cref="Attachments"/>.
    /// </summary>
    /// <remarks>
    ///     Set by S/MIME signing, which must wrap the original content in a <c>multipart/signed</c>
    ///     together with a detached signature computed over that exact content — a transformation that
    ///     cannot be expressed through the plain body properties. Deliberately internal: it is an
    ///     implementation detail of the signing pipeline, not part of the message model callers build.
    /// </remarks>
    internal string? PreRenderedContent { get; init; }
}
