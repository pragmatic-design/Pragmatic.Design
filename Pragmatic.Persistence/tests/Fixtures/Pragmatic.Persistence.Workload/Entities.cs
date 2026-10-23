using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

// The workload every engine reads: a small order domain, the shape an office application has. Plain
// classes with public setters, so EF Core, Dapper and hand-written readers all materialise them the same
// way. [Entity] is the spike's own attribute (same name as the repository's: a drop-in, see Attributes.cs).

public enum OrderStatus
{
    Draft = 0,
    Open = 1,
    Paid = 2,
    Cancelled = 3,
}

[Entity]
public sealed class Customer : ISqlEntity
{
    public Guid Id { get; set; }

    /// <summary>[Interned]: a page of one customer's orders projects the same name on every row (OpenOrderRows); the reader hands out one instance.</summary>
    [Interned]
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>[Interned]: a handful of countries across every customer.</summary>
    [Interned]
    public string Country { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The one-to-many side from the customer: <c>Include(c => c.Orders.Then(o => o.Lines))</c> loads two levels.</summary>
    public List<Order> Orders { get; set; } = [];
}

[Entity]
public sealed class Order : ISqlEntity
{
    public Guid Id { get; set; }

    /// <summary>Indexed in the schema (IX_Orders_CustomerId in schema/workload.schema.json): the generator reads the snapshot, and reports a WHERE on a column without an index as a scan (PRAG3011).</summary>
    public Guid CustomerId { get; set; }

    public string Number { get; set; } = "";

    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>The navigation the join comes from: <c>o.Customer.Name</c> is a JOIN on <see cref="CustomerId" />.</summary>
    public Customer? Customer { get; set; }

    /// <summary>The one-to-many side: <c>Include(o => o.Lines)</c> loads them with a second query on <see cref="OrderLine.OrderId" />.</summary>
    public List<OrderLine> Lines { get; set; } = [];
}

[Entity]
public sealed class OrderLine : ISqlEntity
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>The navigation back to the order: <c>Include(l => l.Order!.Customer)</c> is two joins.</summary>
    public Order? Order { get; set; }
}

/// <summary>A row with a composite key: a tag on an order, unique per (order, tag). Writes match on both columns.</summary>
[Entity]
public sealed class OrderTag : ISqlEntity
{
    [Key]
    public Guid OrderId { get; set; }

    [Key]
    public string Tag { get; set; } = "";

    public string Note { get; set; } = "";

    [Version]
    public int Version { get; set; }

    /// <summary>The notes on this tag: a collection under a composite key, the children's foreign keys those of their navigation back (TagOrderId, TagTag).</summary>
    public List<TagNote> Notes { get; set; } = [];
}

/// <summary>A row whose key the database assigns: identity on SQL Server, generated identity on PostgreSQL, AUTOINCREMENT on SQLite.</summary>
[Entity]
public sealed class Note : ISqlEntity
{
    [Identity]
    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
///     What the write benchmarks and tests touch, so the seed stays intact: an event on an order, with an optimistic-concurrency
///     version. It tracks its own changes too (<see cref="IChangeTracking" />, the repository's shape: there the tracked setters are
///     generated, here <see cref="SetKind" /> is written by hand), so a save writes the modified columns only, at the version.
/// </summary>
[Entity]
public sealed class Event : ISqlEntity, IChangeTracking
{
    private readonly HashSet<string> _modified = [];

    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset At { get; set; }

    [Version]
    public int Version { get; set; }

    /// <summary>The tracked setter: records the change for a save when the value differs.</summary>
    public void SetKind(string value)
    {
        if (Kind == value)
            return;
        Kind = value;
        _modified.Add(nameof(Kind));
    }

    [NotMapped]
    public IReadOnlySet<string> ModifiedProperties => _modified;

    [NotMapped]
    public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();

    [NotMapped]
    public bool IsNew { get; set; }

    public void ResetModifiedProperties() => _modified.Clear();
}

/// <summary>What a grid shows: the order with its customer's name, one row per order. [MapFrom] registers it for the runtime path's Select.</summary>
[MapFrom<Order>]
public sealed class OrderRowDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public decimal Total { get; set; }
    public string CustomerName { get; set; } = "";
}

/// <summary>The same row as a value type: <c>Project&lt;OrderRowStruct&gt;()</c> allocates no object per row, the list holds the values.</summary>
[MapFrom<Order>]
public record struct OrderRowStruct
{
    public required Guid Id { get; init; }
    public required string Number { get; init; }
    public required decimal Total { get; init; }
    public required string CustomerName { get; init; }
}
