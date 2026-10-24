using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Core.Tests.Visibility;

/// <summary>
///     The workload's Charges hierarchy seen through a rule on its root: a charge of 15 or less is not there, for the root and for each
///     derived type (<c>Query&lt;WireCharge&gt;()</c>, <c>OfType&lt;WireCharge&gt;()</c>), unless the chain says <c>WithoutFilters()</c>. Test-only
///     types on the workload's table, named as the workload's (the discriminator is the type's name).
/// </summary>
[Entity]
[Table("Charges")]
[Inheritance(Discriminator = "Kind")]
[VisibleWhen<LargeCharge>]
public class Charge
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
}

[Entity]
public sealed class CardCharge : Charge
{
    public string Last4 { get; set; } = "";
}

[Entity]
public sealed class WireCharge : Charge
{
    public string Iban { get; set; } = "";
    public int Days { get; set; }
}

public sealed class LargeCharge : VisibilityRule<Charge>
{
    public override Expression<Func<Charge, bool>> ToExpression() => charge => charge.Amount > 15m;
}
