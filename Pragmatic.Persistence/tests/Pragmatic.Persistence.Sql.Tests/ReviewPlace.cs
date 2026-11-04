namespace PragmaticEntities.Tests;

/// <summary>A review with the name of the property it is about, read through a declared join (#508): PlaceName is Property.Name, by the alias.</summary>
public sealed class ReviewPlace
{
    public string Text { get; init; } = "";

    public string? PlaceName { get; init; }
}
