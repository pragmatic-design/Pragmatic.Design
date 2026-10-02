namespace Pragmatic.Email.Model;

/// <summary>Text block (paragraph) in an email.</summary>
public sealed record EmailTextNode : EmailNode
{
    /// <summary>Text content. HTML-encoded by default; set <see cref="AllowHtml"/> to emit it verbatim.</summary>
    public required string Content { get; init; }

    /// <summary>
    ///     When true, <see cref="Content"/> is emitted verbatim (allowing simple inline HTML such as
    ///     &lt;b&gt;, &lt;i&gt;, &lt;a&gt;, &lt;br&gt;). Default false: content is HTML-encoded so text
    ///     interpolated from untrusted data sources cannot inject markup/script into the email.
    /// </summary>
    public bool AllowHtml { get; init; }

    /// <summary>Font size in px. Null = inherit from EmailModel.</summary>
    public int? FontSize { get; init; }

    /// <summary>Text color (hex). Null = inherit.</summary>
    public string? Color { get; init; }

    /// <summary>Text alignment.</summary>
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Left;

    /// <summary>Line height (e.g. "1.5", "24px").</summary>
    public string? LineHeight { get; init; }
}
