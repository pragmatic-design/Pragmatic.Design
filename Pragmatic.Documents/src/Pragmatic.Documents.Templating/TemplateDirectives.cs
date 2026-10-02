namespace Pragmatic.Documents.Templating;

/// <summary>
/// Structural directives applicable to any template node.
/// </summary>
public sealed record TemplateDirectives
{
    /// <summary>
    /// Conditional: show node only if expression evaluates to truthy.
    /// Example: <c>"order.hasDiscount"</c>, <c>"total > 1000"</c>
    /// </summary>
    public string? If { get; init; }

    /// <summary>
    /// Iteration: repeat this node for each item in a collection.
    /// Syntax: <c>"item in order.items"</c>
    /// </summary>
    public string? For { get; init; }
}
