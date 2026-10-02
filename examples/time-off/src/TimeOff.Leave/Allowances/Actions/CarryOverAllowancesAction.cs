using Pragmatic.Validation.Types;
using TimeOff.Leave.Allowances.Mutations;
using TimeOff.Leave.Allowances.Queries;

namespace TimeOff.Leave.Allowances.Actions;

/// <summary>
///     At year end HR carries over what each employee has left of a kind into the next year, up to a cap
///     the company sets.
/// </summary>
/// <remarks>
///     <para>
///         What is left is the free amount a submission counts — <c>Remaining − Pending</c>, what is asked
///         for and not decided being as good as taken — read from <see cref="ListYearBalancesQuery" />, so
///         the database adds the requests up. Never less than zero, never more than the cap.
///     </para>
///     <para>
///         It sets, it never adds: the next year's <c>CarriedOver</c> becomes that amount, however many
///         times this runs. The next year's allowance is created with nothing granted when it does not
///         exist yet, and when it does only its carry-over changes — what HR granted for the year stays.
///         Both through the allowance's own mutations, whose writes this operation commits together.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.Allowance.Update)]
[ProcessesData<Allowance>]
[ProcessesData<Employee>]
[LoadEntity<AbsenceKind>(nameof(AbsenceKindId))]
[Endpoint(HttpVerb.Post, "api/allowances/carry-over")]
public partial class CarryOverAllowancesAction : DomainAction<IReadOnlyList<CarryOverDto>, NotFoundError>
{
    private IRepository<Allowance> _allowances = null!;
    private ILeaveInternalActions _leave = null!;

    public required Guid AbsenceKindId { get; init; }

    public required int FromYear { get; init; }

    /// <summary>The most that moves into the next year, in the kind's unit.</summary>
    public required decimal Cap { get; init; }

    public override async Task<Result<IReadOnlyList<CarryOverDto>, IError>> Execute(CancellationToken ct = default)
    {
        if (!_absenceKind.UsesAllowance)
            return ValidationError.For(nameof(AbsenceKindId), T.Validation.CarryOver.KindUsesNoAllowance);

        var balances = await _allowances
            .RunAsync(new ListYearBalancesQuery { AbsenceKindId = AbsenceKindId, Year = FromYear }, ct)
            .ConfigureAwait(false);

        // Every allowance of the kind next year, not only the ones of these employees: the lookup below
        // is by employee, and one of someone without a balance this year is never asked for.
        var nextYear = FromYear + 1;
        var existing = (await _allowances.FindForKindAndYearAsync(AbsenceKindId, nextYear, ct).ConfigureAwait(false))
            .ToDictionary(a => a.EmployeeId);

        var carried = new List<CarryOverDto>(balances.Count);
        foreach (var balance in balances.OrderBy(b => b.EmployeeNumber, StringComparer.Ordinal))
        {
            var amount = Math.Min(Math.Max(balance.Remaining - balance.Pending, 0m), Cap);

            var written = existing.TryGetValue(balance.EmployeeId, out var next)
                ? await _leave.Allowances
                    .CorrectAllowance(new CorrectAllowanceMutation { Id = next.Id, CarriedOver = amount }, next, ct)
                    .ConfigureAwait(false)
                : await _leave.Allowances
                    .GrantAllowance(new GrantAllowanceMutation
                    {
                        EmployeeId = balance.EmployeeId,
                        AbsenceKindId = AbsenceKindId,
                        Year = nextYear,
                        Entitled = 0m,
                        CarriedOver = amount
                    }, ct)
                    .ConfigureAwait(false);
            if (written.IsFailure)
                return Result<IReadOnlyList<CarryOverDto>, IError>.Failure(written.Error);

            carried.Add(new CarryOverDto(balance.EmployeeId, balance.EmployeeNumber, amount));
        }

        return carried;
    }
}
