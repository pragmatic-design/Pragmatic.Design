using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>A late check-out's charge: a derived type with a private setter of its own.</summary>
[Entity]
public partial class LateCharge : Charge
{
    public int HoursLate { get; private set; }

    public void Late(int hours) => HoursLate = hours;
}
