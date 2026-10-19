namespace Pragmatic.Persistence.Fuzz;

/// <summary>The entity the grid harnesses query: a number, a string, a flag, a date and an enum, the column kinds a grid filters on.</summary>
public sealed class FuzzOrder
{
    public int Number { get; set; }

    public string Name { get; set; } = "";

    public bool Paid { get; set; }

    public DateTime PlacedAt { get; set; }

    public FuzzOrderState State { get; set; }

    /// <summary>A few rows, so that a query that runs is evaluated against something.</summary>
    public static IQueryable<FuzzOrder> Rows() => new[]
    {
        new FuzzOrder { Number = 1, Name = "Ada", Paid = true, PlacedAt = new DateTime(2026, 1, 1), State = FuzzOrderState.Open },
        new FuzzOrder { Number = 20, Name = "Grace", Paid = false, PlacedAt = new DateTime(2026, 6, 1), State = FuzzOrderState.Shipped },
        new FuzzOrder { Number = 300, Name = "", Paid = true, PlacedAt = DateTime.MinValue, State = FuzzOrderState.Cancelled },
    }.AsQueryable();
}
