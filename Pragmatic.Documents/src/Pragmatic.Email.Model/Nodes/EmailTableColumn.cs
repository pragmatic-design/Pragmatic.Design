namespace Pragmatic.Email.Model;

/// <summary>Email table column definition.</summary>
public sealed record EmailTableColumn
{
    /// <summary>Width in px. Null = auto.</summary>
    public int? Width { get; init; }

    /// <summary>Text alignment for this column.</summary>
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Left;
}
