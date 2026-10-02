namespace Pragmatic.Email.Model;

/// <summary>Padding in pixels.</summary>
public sealed record EmailPadding
{
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public int Left { get; init; }

    public static EmailPadding Default => new() { Top = 20, Right = 20, Bottom = 20, Left = 20 };
    public static EmailPadding None => new();

    /// <summary>Uniform padding.</summary>
    public static EmailPadding All(int value) => new() { Top = value, Right = value, Bottom = value, Left = value };
}
