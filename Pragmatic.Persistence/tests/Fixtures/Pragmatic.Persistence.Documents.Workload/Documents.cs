using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Documents.Workload;

/// <summary>An order as one document: its lines inside, the SKUs it holds as an array of values, where it ships (absent for some).</summary>
[Document("orders", PartitionKey = nameof(CustomerId))]
public sealed partial class OrderDocument
{
    [Key]
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string Number { get; set; } = "";

    public OrderStatus Status { get; set; }

    public decimal Total { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public List<LineDocument> Lines { get; set; } = [];

    public List<string> Skus { get; set; } = [];

    public ShippingAddress? ShipTo { get; set; }

    [Version]
    public int Version { get; set; }
}

[Embedded]
public sealed partial class LineDocument
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = "";

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

[Embedded]
public sealed partial class ShippingAddress
{
    public string Country { get; set; } = "";

    public string City { get; set; } = "";

    public int? Floor { get; set; }
}

[Document("customers")]
public sealed partial class CustomerDocument
{
    [Key]
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Email { get; set; } = "";

    public string Country { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The SQL workload's seed as documents: deterministic, the same values (minutes for the instants: BSON keeps milliseconds).</summary>
public static class DocumentSeed
{
    private static readonly string[] Cities = ["Rome", "Berlin", "Paris", "Madrid", "Amsterdam", "Vienna", "Zurich", "Brussels"];

    public static List<CustomerDocument> Customers(Seed seed)
        => seed.Customers.Select(c => new CustomerDocument { Id = c.Id, Name = c.Name, Email = c.Email, Country = c.Country, CreatedAt = c.CreatedAt }).ToList();

    public static List<OrderDocument> Orders(Seed seed)
    {
        var countries = seed.Customers.ToDictionary(c => c.Id, c => c.Country);
        var lines = seed.Lines.ToLookup(l => l.OrderId);
        return seed.Orders.Select((o, i) => new OrderDocument
        {
            Id = o.Id,
            CustomerId = o.CustomerId,
            Number = o.Number,
            Status = o.Status,
            Total = o.Total,
            CreatedAt = o.CreatedAt,
            IsDeleted = o.IsDeleted,
            Lines = lines[o.Id].Select(l => new LineDocument { Id = l.Id, Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
            Skus = lines[o.Id].Select(l => l.Sku).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
            ShipTo = i % 5 == 0 ? null : new ShippingAddress { Country = countries[o.CustomerId], City = Cities[i % Cities.Length], Floor = i % 3 == 0 ? null : i % 7 },
        }).ToList();
    }
}
