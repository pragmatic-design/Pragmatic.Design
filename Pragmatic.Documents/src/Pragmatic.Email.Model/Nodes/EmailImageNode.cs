namespace Pragmatic.Email.Model;

/// <summary>Image in an email. Must use absolute URLs (no base64 for email).</summary>
public sealed record EmailImageNode : EmailNode
{
    /// <summary>Image source URL (must be absolute, publicly accessible).</summary>
    public required string Source { get; init; }

    /// <summary>Alt text (required for accessibility).</summary>
    public required string Alt { get; init; }

    /// <summary>Width in pixels. Null = auto.</summary>
    public int? Width { get; init; }

    /// <summary>Height in pixels. Null = auto.</summary>
    public int? Height { get; init; }

    /// <summary>Optional link URL when the image is clicked.</summary>
    public string? Link { get; init; }

    /// <summary>Alignment.</summary>
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Center;
}
