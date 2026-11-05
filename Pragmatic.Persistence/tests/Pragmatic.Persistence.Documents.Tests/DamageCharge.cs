using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>A damage's charge: the other derived type.</summary>
[Entity]
public partial class DamageCharge : Charge
{
    public string Item { get; private set; } = "";

    public void Damaged(string item) => Item = item;
}
