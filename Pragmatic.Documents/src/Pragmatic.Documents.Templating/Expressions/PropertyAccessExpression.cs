namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Property access path: <c>customer.address.city</c></summary>
/// <remarks>
///     The path is split into its segments once, when the template is parsed: a template reads the same path
///     for every row of a table, and splitting it on each read was a third of what a read allocated (#261).
///     Equality stays the path's, so two expressions for the same path are equal whatever array holds the
///     segments.
/// </remarks>
public sealed record PropertyAccessExpression(string Path) : TemplateExpression
{
    /// <summary>The path's dot-separated segments: the source name first, then each property.</summary>
    internal string[] Segments { get; } = Path.Split('.');

    public bool Equals(PropertyAccessExpression? other) => other is not null && Path == other.Path;

    public override int GetHashCode() => Path.GetHashCode(StringComparison.Ordinal);
}
