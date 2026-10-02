using Pragmatic.Documents.Csv;

namespace Pragmatic.Documents.Csv.Generator.Tests;

[CsvSerializable]
public partial record OrderDto
{
    [CsvColumn("N. Ordine")]
    public string Number { get; init; } = "";

    [CsvColumn("Data", Format = "dd/MM/yyyy")]
    public DateTime Date { get; init; }

    [CsvColumn("Totale", Format = "#,##0.00")]
    public decimal Total { get; init; }

    public bool Active { get; init; }
}

/// <summary>
///     A computed column: written to the file, not read back — it recomputes from the columns that are.
///     A generated Read that assigned it would not compile (CS0200).
/// </summary>
[CsvSerializable]
public partial record InvoiceLineDto
{
    public decimal Net { get; init; }
    public decimal Tax { get; init; }
    public decimal Total => Net + Tax;
}

[CsvSerializable]
public partial record SimpleDto
{
    public string Name { get; init; } = "";
    public int Age { get; init; }
    public double Score { get; init; }
}

[CsvSerializable]
public partial record NullableDto
{
    public string Name { get; init; } = "";
    public int? OptionalAge { get; init; }
    public decimal? OptionalTotal { get; init; }
}

[CsvSerializable]
public partial record IgnoreDto
{
    public string Name { get; init; } = "";

    [CsvColumn(Ignore = true)]
    public string Secret { get; init; } = "";

    public int Value { get; init; }
}

public enum OrderStatus
{
    Pending,
    Shipped,
    Delivered
}

[CsvSerializable]
public partial record TypedDto
{
    public string Name { get; init; } = "";
    public OrderStatus Status { get; init; }
    public OrderStatus? OptionalStatus { get; init; }
    public Guid Id { get; init; }
    public TimeSpan Duration { get; init; }
}
