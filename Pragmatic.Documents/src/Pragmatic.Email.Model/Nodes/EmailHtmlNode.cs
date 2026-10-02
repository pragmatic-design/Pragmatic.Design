namespace Pragmatic.Email.Model;

/// <summary>
///     Raw HTML block in an email. Use sparingly — email clients have limited HTML support.
/// </summary>
/// <remarks>
///     <para>
///         <b>Trust contract.</b> The node is <b>safe by default</b>: when <see cref="IsTrusted" />
///         is <c>false</c> (the default), <see cref="Html" /> is HTML-encoded so the text is shown
///         literally and cannot inject markup/script. Only when you explicitly set
///         <see cref="IsTrusted" /> to <c>true</c> is <see cref="Html" /> emitted <b>verbatim</b>
///         with no sanitization — and then the caller is responsible for ensuring the markup is
///         well-formed, email-safe, and free of attacker-controlled content.
///     </para>
/// </remarks>
public sealed record EmailHtmlNode : EmailNode
{
    /// <summary>
    ///     The HTML content. HTML-encoded when <see cref="IsTrusted" /> is <c>false</c> (the default);
    ///     emitted verbatim only when <see cref="IsTrusted" /> is <c>true</c>.
    /// </summary>
    public required string Html { get; init; }

    /// <summary>
    ///     Whether <see cref="Html" /> is trusted, pre-sanitized markup that may be emitted verbatim.
    ///     Defaults to <c>false</c> (safe): untrusted content is HTML-encoded. Set to <c>true</c> only
    ///     for markup you fully control.
    /// </summary>
    public bool IsTrusted { get; init; }
}
