namespace Pragmatic.Configuration.Benchmarks.Workload;

/// <summary>
///     A tenant's billing settings: ten scalar properties, the shape the generator writes a typed binder for.
/// </summary>
[Configuration]
public partial class BillingOptions
{
    public string Currency { get; set; } = "EUR";
    public decimal TaxRate { get; set; }
    public int PaymentTermDays { get; set; } = 30;
    public bool SendReminders { get; set; }
    public int ReminderEveryDays { get; set; } = 7;
    public string InvoicePrefix { get; set; } = "INV";
    public decimal LateFeeRate { get; set; }
    public int MaxLines { get; set; } = 500;
    public string Region { get; set; } = "default";
    public bool RoundToCents { get; set; } = true;
}
