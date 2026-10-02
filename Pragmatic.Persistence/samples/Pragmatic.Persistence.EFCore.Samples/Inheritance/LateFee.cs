namespace Pragmatic.Persistence.EFCore.Samples.Inheritance;

/// <summary>A fixed late-payment fee. TPH discriminator value "LateFee".</summary>
public partial class LateFee : Charge
{
    public int DaysLate { get; set; }
}
