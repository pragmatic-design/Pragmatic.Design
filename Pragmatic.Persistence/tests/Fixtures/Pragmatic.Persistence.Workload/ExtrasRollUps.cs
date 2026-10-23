using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

// A parent with stored aggregates over its children ([RollUp<TChild>], the repository's attribute): the children's writes keep them.
// The tables come from ExtrasSchema, the aggregate columns with a default of 0.

/// <summary>A cart whose units, line count and amount are kept by its lines' writes: read back, never written by the cart's own.</summary>
[Entity]
[Table("Carts")]
public sealed class Cart : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];
    private readonly HashSet<string> _collections = [];

    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [RollUp<CartLine>(nameof(CartLine.Quantity))]
    public int Units { get; set; }

    [RollUp<CartLine>("", RollUpAggregation.Count)]
    public int LineCount { get; set; }

    [RollUp<CartLine>(nameof(CartLine.Price))]
    public decimal Amount { get; set; }

    public List<CartLine> Lines { get; set; } = [];

    public void SetName(string value)
    {
        if (Name == value)
            return;
        Name = value;
        _modified.Add(nameof(Name));
    }

    public void AddLine(CartLine line)
    {
        line.CartId = Id;
        Lines.Add(line);
        _collections.Add(nameof(Lines));
    }

    public void RemoveLine(CartLine line)
    {
        if (Lines.Remove(line))
            _collections.Add(nameof(Lines));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified => _collections;

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties()
    {
        _modified.Clear();
        _collections.Clear();
    }
}

/// <summary>A line of a cart: its quantity and price feed the cart's aggregates.</summary>
[Entity]
[Table("CartLines")]
public sealed class CartLine : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];

    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }

    public void SetQuantity(int value)
    {
        if (Quantity == value)
            return;
        Quantity = value;
        _modified.Add(nameof(Quantity));
    }

    public void MoveTo(Guid cart)
    {
        if (CartId == cart)
            return;
        CartId = cart;
        _modified.Add(nameof(CartId));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties() => _modified.Clear();
}
