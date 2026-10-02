namespace Pragmatic.Documents.Model;

/// <summary>Image element.</summary>
public sealed record ImageNode : DocumentNode
{
    /// <summary>
    /// Image source. Supported formats:
    /// <list type="bullet">
    ///   <item><c>resource:name</c> — resolved from renderer-specific resource dictionary (all renderers)</item>
    ///   <item><c>data:image/png;base64,...</c> — inline base64 (DOCX, Email)</item>
    ///   <item>An http(s) URL — Email only: the mail client fetches it. The DOCX and PDF renderers
    ///   embed bytes they are given and fetch nothing.</item>
    /// </list>
    /// A source a renderer cannot resolve fails the render — DOCX throws
    /// <see cref="System.InvalidOperationException"/> naming the source and the resources it had, PDF
    /// throws its render exception — rather than producing a document without the image.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Alt text for accessibility.</summary>
    public string? Alt { get; init; }

    /// <summary>Width in mm. Null = auto.</summary>
    public double? Width { get; init; }

    /// <summary>Height in mm. Null = auto (preserves aspect ratio).</summary>
    public double? Height { get; init; }
}
