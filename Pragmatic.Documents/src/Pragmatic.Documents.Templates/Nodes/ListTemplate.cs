namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>List template with optional data source for dynamic items.</summary>
public sealed record ListTemplate : DocumentNodeTemplate
{
    public bool Ordered { get; init; }
    public IReadOnlyList<ListItemTemplate> Items { get; init; } = [];

    /// <summary>Data source path for dynamic items.</summary>
    public string? DataSource { get; init; }

    /// <summary>Item template applied per item when DataSource is set.</summary>
    public ListItemTemplate? ItemTemplate { get; init; }
}
