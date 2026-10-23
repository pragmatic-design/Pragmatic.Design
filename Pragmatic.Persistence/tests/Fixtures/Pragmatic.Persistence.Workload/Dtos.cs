using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

// Declared projections with collections and aggregates: Project<OrderWithLinesDto>() reads the order's row, its
// lines projected into OrderLineDto in a companion statement of the same command, and three aggregates over the
// lines as correlated subqueries. No entity is materialised; EF Core's twin is a Select with a nested Select.

/// <summary>One line of an order, as a grid shows it: the child DTO of <see cref="OrderWithLinesDto" />.</summary>
[MapFrom<OrderLine>]
public sealed class OrderLineDto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}

/// <summary>An order with its lines and their aggregates, in one round trip: the key (<c>Id</c>) is what the loader groups the lines on.</summary>
[MapFrom<Order>]
public sealed class OrderWithLinesDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string CustomerName { get; set; } = "";

    /// <summary>By convention, <c>{Collection}Count</c>: <c>(SELECT COUNT(*) FROM "OrderLines" c WHERE c."OrderId" = t."Id")</c>.</summary>
    public int LinesCount { get; set; }

    /// <summary>A sum over a child column: <c>COALESCE(SUM(c."Quantity"), 0)</c>, zero without lines.</summary>
    [MapProperty("Lines.Sum(Quantity)")]
    public int Units { get; set; }

    /// <summary>A maximum over a child column: NULL without lines, so the property is nullable.</summary>
    [MapProperty("Lines.Max(UnitPrice)")]
    public decimal? TopPrice { get; set; }

    /// <summary>The collection, projected into the child DTO by its own <c>[MapFrom]</c>.</summary>
    public List<OrderLineDto> Lines { get; set; } = [];
}

/// <summary>Two levels of collections: a customer, its orders with their lines. Three statements, one command.</summary>
[MapFrom<Customer>]
public sealed class CustomerWithOrdersDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int OrdersCount { get; set; }
    public List<OrderWithLinesDto> Orders { get; set; } = [];
}
