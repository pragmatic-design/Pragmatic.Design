using Pragmatic.Validation.Types;

namespace TimeOff.Leave.Entities;

/// <summary>
///     A kind of absence — vacation, sick leave, personal hours — and how it is counted.
/// </summary>
/// <remarks>
///     The name is content, not a message: HR writes it in each language, and a reader gets it in
///     theirs. The code is how the kind is referred to everywhere else.
/// </remarks>
[Entity]
[Auditable]
public partial class AbsenceKind : IEntity
{
    /// <summary>A stable, upper-case identifier: <c>VACATION</c>, <c>SICK</c>, <c>PERSONAL_HOURS</c>.</summary>
    [LogicKey]
    [Required]
    [MaxLength(40)]
    public string Code { get; private set; } = "";

    public LocalizedString Name { get; private set; } = new();

    public AbsenceUnit Unit { get; private set; }

    /// <summary>Whether a request of this kind is taken from the employee's yearly allowance.</summary>
    public bool UsesAllowance { get; private set; }

    /// <summary>
    ///     Hours are only ever granted as a budget: a kind counted in hours has to draw from an
    ///     allowance, or nothing would bound it.
    /// </summary>
    internal ValidationError CheckCountingRule() =>
        Unit == AbsenceUnit.Hours && !UsesAllowance
            ? ValidationError.For(nameof(UsesAllowance), T.Validation.AbsenceKind.HoursNeedAnAllowance)
            : ValidationError.Valid;
}
