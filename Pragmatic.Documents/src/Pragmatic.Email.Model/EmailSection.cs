namespace Pragmatic.Email.Model;

/// <summary>
/// A full-width section in the email (rendered as a table row).
/// Contains one or more columns.
/// </summary>
public sealed record EmailSection
{
    /// <summary>Background color for this section (hex).</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>Padding in pixels [top, right, bottom, left].</summary>
    public EmailPadding Padding { get; init; } = EmailPadding.Default;

    /// <summary>Columns in this section. Each column is a fraction of the total width.</summary>
    public IReadOnlyList<EmailColumn> Columns { get; init; } = [];
}
