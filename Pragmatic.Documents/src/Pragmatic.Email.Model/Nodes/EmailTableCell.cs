namespace Pragmatic.Email.Model;

/// <summary>A cell in an email table.</summary>
public sealed record EmailTableCell
{
    /// <summary>Cell text content.</summary>
    public string Content { get; init; } = "";

    /// <summary>Column span.</summary>
    public int ColSpan { get; init; } = 1;

    /// <summary>Bold text.</summary>
    public bool Bold { get; init; }

    /// <summary>Text color (hex). Null = inherit.</summary>
    public string? Color { get; init; }

    /// <summary>Override alignment for this cell.</summary>
    public EmailTextAlign? Align { get; init; }
}
