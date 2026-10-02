namespace TimeOff.Leave.Entities;

/// <summary>
///     A day the company is closed that the national calendar does not know: a closure, or the patron
///     saint's day. HR keeps the list; the national holidays are computed, not stored.
/// </summary>
[Entity]
[Auditable]
public partial class CompanyHoliday : IEntity
{
    [LogicKey]
    public DateOnly Date { get; private set; }

    public LocalizedString Name { get; private set; } = new();

    public CompanyHolidayKind Kind { get; private set; }
}
