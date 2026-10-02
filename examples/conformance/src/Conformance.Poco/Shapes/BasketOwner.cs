namespace Conformance.Poco.Shapes;

/// <summary>
///     The single navigation: it serves the one-to-one merge case and the <c>null</c> one.
/// </summary>
public sealed class BasketOwner
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}
