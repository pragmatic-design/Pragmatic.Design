namespace Showcase.Billing.Dtos;

/// <summary>
/// Invoice DTO with Money value type.
/// Demonstrates: Money (amount + currency as a single value type).
/// </summary>
public sealed record InvoiceMoneyDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = "";

    /// <summary>
    /// Total amount as Money value type.
    /// Demonstrates: Money — prevents mixing currencies, provides formatting.
    /// </summary>
    public Money Total { get; init; }

    /// <summary>
    /// Tax amount as Money value type.
    /// </summary>
    public Money Tax { get; init; }

    /// <summary>
    /// Formatted total for display (e.g. "€ 1.234,50").
    /// Demonstrates: Money.Format() for locale-aware rendering.
    /// </summary>
    public string TotalFormatted => Total.Format();

    public string Status { get; init; } = "";
}
