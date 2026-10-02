namespace Pragmatic.Persistence.EFCore.Samples.Inheritance;

/// <summary>A metered usage charge. TPH discriminator value "UsageCharge".</summary>
public partial class UsageCharge : Charge
{
    public int Units { get; set; }
    public decimal UnitRate { get; set; }
}
