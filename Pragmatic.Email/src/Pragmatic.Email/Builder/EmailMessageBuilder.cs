namespace Pragmatic.Email.Builder;

/// <summary>
///     Fluent builder for constructing <see cref="EmailMessage"/> instances.
/// </summary>
public sealed class EmailMessageBuilder
{
    private EmailAddress? _from;
    private readonly List<EmailAddress> _to = [];
    private readonly List<EmailAddress> _cc = [];
    private readonly List<EmailAddress> _bcc = [];
    private EmailAddress? _replyTo;
    private string? _subject;
    private string? _textBody;
    private string? _htmlBody;
    private readonly List<EmailAttachment> _attachments = [];
    private readonly Dictionary<string, string> _headers = [];

    /// <summary>Sets the sender address. Validates format immediately.</summary>
    public EmailMessageBuilder From(string address, string? name = null)
    {
        _from = EmailAddress.Validated(address, name);
        return this;
    }

    /// <summary>Adds a primary recipient. Validates format immediately.</summary>
    public EmailMessageBuilder To(string address, string? name = null)
    {
        _to.Add(EmailAddress.Validated(address, name));
        return this;
    }

    /// <summary>Adds a CC recipient. Validates format immediately.</summary>
    public EmailMessageBuilder Cc(string address, string? name = null)
    {
        _cc.Add(EmailAddress.Validated(address, name));
        return this;
    }

    /// <summary>Adds a BCC recipient. Validates format immediately.</summary>
    public EmailMessageBuilder Bcc(string address, string? name = null)
    {
        _bcc.Add(EmailAddress.Validated(address, name));
        return this;
    }

    /// <summary>Sets the Reply-To address. Validates format immediately.</summary>
    public EmailMessageBuilder ReplyTo(string address, string? name = null)
    {
        _replyTo = EmailAddress.Validated(address, name);
        return this;
    }

    /// <summary>Sets the email <c>Subject</c> header. Accepts any string (no trimming or validation).</summary>
    public EmailMessageBuilder Subject(string subject)
    {
        _subject = subject;
        return this;
    }

    /// <summary>Sets the plain-text body part. Co-existing with <see cref="HtmlBody"/> produces a multipart/alternative message.</summary>
    public EmailMessageBuilder TextBody(string body)
    {
        _textBody = body;
        return this;
    }

    /// <summary>Sets the HTML body part. Co-existing with <see cref="TextBody"/> produces a multipart/alternative message.</summary>
    public EmailMessageBuilder HtmlBody(string body)
    {
        _htmlBody = body;
        return this;
    }

    /// <summary>Appends an attachment with the given <paramref name="fileName"/>, raw bytes and MIME <paramref name="contentType"/>.</summary>
    public EmailMessageBuilder Attach(string fileName, ReadOnlyMemory<byte> data, string contentType)
    {
        _attachments.Add(new EmailAttachment
        {
            FileName = fileName,
            ContentType = contentType,
            Data = data,
        });
        return this;
    }

    public EmailMessageBuilder InlineImage(string contentId, ReadOnlyMemory<byte> data, string contentType = "image/png")
    {
        _attachments.Add(new EmailAttachment
        {
            FileName = contentId,
            ContentType = contentType,
            Data = data,
            IsInline = true,
            ContentId = contentId,
        });
        return this;
    }

    public EmailMessageBuilder Header(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    public EmailMessage Build()
    {
        if (_from is null) throw new InvalidOperationException("From address is required.");
        if (_to.Count == 0) throw new InvalidOperationException("At least one To address is required.");
        if (_subject is null) throw new InvalidOperationException("Subject is required.");
        if (_textBody is null && _htmlBody is null) throw new InvalidOperationException("At least TextBody or HtmlBody is required.");

        return new EmailMessage
        {
            From = _from.Value,
            To = _to,
            Cc = _cc,
            Bcc = _bcc,
            ReplyTo = _replyTo,
            Subject = _subject,
            TextBody = _textBody,
            HtmlBody = _htmlBody,
            Attachments = _attachments,
            Headers = _headers,
        };
    }
}
