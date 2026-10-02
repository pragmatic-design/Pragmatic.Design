namespace Pragmatic.Documents.Model;

/// <summary>Page margins in millimeters.</summary>
public sealed record Margins
{
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }
    public double Left { get; init; }

    /// <summary>Default A4 margins (25mm all sides).</summary>
    public static Margins Default => new() { Top = 25, Right = 25, Bottom = 25, Left = 25 };

    /// <summary>Narrow margins (12.7mm).</summary>
    public static Margins Narrow => new() { Top = 12.7, Right = 12.7, Bottom = 12.7, Left = 12.7 };

    /// <summary>No margins.</summary>
    public static Margins None => new();
}
