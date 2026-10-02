using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Dtos;

// ═══════════════════════════════════════════════════════════════════════════════
// Struct Mapping - Zero allocation for hot paths
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
///     Entity for struct mapping demo.
/// </summary>
public class Coordinate
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
    public string Label { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>
///     Record struct DTO - value type, no heap allocation.
///     Ideal for high-performance scenarios and hot paths.
/// </summary>
[MapFrom<Coordinate>]
[GenerateProjection]
public partial record struct CoordinateDto
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? Altitude { get; init; }
    public string Label { get; init; }
}

/// <summary>
///     Regular struct DTO (non-record).
/// </summary>
[MapFrom<Coordinate>]
public partial struct PointDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Money Value Object - Struct for financial data
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
///     Entity with money value.
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTime DueDate { get; set; }
}

/// <summary>
///     Invoice DTO with embedded money struct.
/// </summary>
[MapFrom<Invoice>]
public partial record InvoiceDto
{
    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = "";

    // Direct mapping of amount and currency
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";

    // Formatted due date
    [MapProperty(nameof(Invoice.DueDate), Format = "d")]
    public string DueDate { get; init; } = "";
}