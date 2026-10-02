namespace Pragmatic.Documents.Model;

/// <summary>Table column definition.</summary>
public sealed record TableColumn
{
    /// <summary>Column width in mm. Null = auto-distribute.</summary>
    public double? Width { get; init; }

    /// <summary>Text alignment for this column.</summary>
    public TextAlign? Align { get; init; }
}
