using System.Text;
using Pragmatic.Cryptography;
using Pragmatic.Ensure;
using Pragmatic.Privacy;
using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Types;
using Pragmatic.Validation.Types;
using TimeOff.Leave.Allowances.Queries;
using TimeOff.Leave.Infrastructure.Calendar;
using TimeOff.Leave.Infrastructure.Validation;

namespace TimeOff.Leave.LeaveRequests.Actions;

/// <summary>
///     An employee asks for time off: a period of days, or some hours of one day.
/// </summary>
/// <remarks>
///     <para>
///         The kind of absence is loaded by the invoker (<c>[LoadEntity]</c>), after the request is
///         validated and authorized and before <c>Execute</c>: one nobody defined is 404, and <c>Execute</c>
///         never sees it missing.
///     </para>
///     <para>
///         What the request says is checked before it runs. On its properties, what can be judged on the
///         request alone: the period in order and within one year — the allowance is yearly — and the
///         hours within a working day. In <see cref="ValidateLoaded" />, right after the kind is loaded,
///         what depends on how it is counted: hours of one day, or no hours at all.
///     </para>
///     <para>
///         Here, what only the domain can answer. The working days of the period are counted and fixed
///         on the request: weekends, the Italian national holidays and the company's own holidays are not
///         taken, and a period with none is refused. Refused too, each with its own code: an overlap with
///         a request of the employee's that still holds its days (<see cref="OverlappingLeaveRequestError" />),
///         and more than is left of the allowance, pending requests counted as taken
///         (<see cref="AllowanceExceededError" />).
///     </para>
///     <para>
///         The requester is the caller: nobody asks for leave on someone else's behalf. Their balances are
///         read by the invoker too, through <see cref="GetMyBalancesQuery" /> (<c>[LoadFrom]</c>) — the query's
///         own pipeline and permission, as over HTTP.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.LeaveRequest.Create)]
[LoadEntity<AbsenceKind>(nameof(AbsenceKindId))]
[LoadCurrentUser]
[Endpoint(HttpVerb.Post, "api/leave-requests")]
public partial class SubmitLeaveRequestAction
    : DomainAction<LeaveRequestDto, NotFoundError, OverlappingLeaveRequestError, AllowanceExceededError>
{
    private const int HoursInAWorkingDay = 8;

    private ICurrentUser _currentUser = null!;
    private IRepository<LeaveRequest> _requests = null!;
    private IReadRepository<CompanyHoliday> _companyHolidays = null!;
    private ITemporalCalculator _calendar = null!;

    /// <summary>What encrypts the reason under the employee's own key, and the registry that names them.</summary>
    /// <remarks>
    ///     The protector takes a subject <b>reference</b>, never an identity, so the registry comes with
    ///     it. Both are injected here rather than reached from the entity: an entity performs no
    ///     cryptography, and a property that encrypted itself would need a key it must not hold.
    /// </remarks>
    private ISubjectDataProtector _protector = null!;
    private ISubjectRegistry _subjects = null!;

    public required Guid AbsenceKindId { get; init; }

    public required DateOnly From { get; init; }

    [GreaterThanOrEqualProperty(nameof(From), MessageKey = TKeys.Validation.LeaveRequest.EndsBeforeItStarts)]
    [SameYearAs(nameof(From), MessageKey = TKeys.Validation.LeaveRequest.SpansTwoYears)]
    public required DateOnly To { get; init; }

    /// <summary>For a kind counted in hours: how many, on the one day of the request.</summary>
    [GreaterThan(0, MessageKey = TKeys.Validation.LeaveRequest.HoursOutOfRange)]
    [LessThanOrEqual(HoursInAWorkingDay, MessageKey = TKeys.Validation.LeaveRequest.HoursOutOfRange)]
    public decimal? Hours { get; init; }

    public string? Reason { get; init; }

    /// <summary>The year the request counts in — the input <see cref="Balances" /> is read for.</summary>
    private int Year => From.Year;

    /// <summary>The caller's balances for <see cref="Year" />, read before <c>Execute</c>.</summary>
    [LoadFrom<GetMyBalancesQuery>]
    private IReadOnlyList<AllowanceBalanceDto> Balances { get; set; } = [];

    public override async Task<Result<LeaveRequestDto, IError>> Execute(CancellationToken ct = default)
    {
        var employee = _currentEmployee;

        var amount = _absenceKind.Unit == AbsenceUnit.Hours
            ? HoursOnTheDay()
            : await WorkingDaysAsync(ct).ConfigureAwait(false);
        if (amount.IsFailure)
            return Result<LeaveRequestDto, IError>.Failure(amount.Error);

        var overlapping = await _requests
            .FirstOrDefaultAsync(
                LeaveRequestSpecifications.RequestedBy(employee.Id)
                & LeaveRequestSpecifications.Standing
                & LeaveRequestSpecifications.Overlapping(From, To),
                ct)
            .ConfigureAwait(false);
        if (overlapping is not null)
            return new OverlappingLeaveRequestError { OverlappingRequestId = overlapping.Id };

        Guid? allowanceId = null;
        if (_absenceKind.UsesAllowance)
        {
            // Pending counts as taken here: a request may not ask for days another one, still
            // undecided, already asked for.
            var balance = Balances.FirstOrDefault(b => b.AbsenceKindId == AbsenceKindId);
            var free = balance is null ? 0m : balance.Remaining - balance.Pending;
            if (balance is null || amount.Value > free)
                return new AllowanceExceededError { Requested = amount.Value, Remaining = free };

            allowanceId = balance.Id;
        }

        // ⚠️ Encrypted here and not in the entity: a reason for leave can say what is wrong with
        // someone, so it is stored under the employee's own key and erased by destroying it. The key is
        // per subject, which is why this needs their reference — and the reference is allocated on
        // first sight, which this is: an employee asking for leave is acting.
        var reason = string.IsNullOrWhiteSpace(Reason)
            ? ProtectedValue.None
            : new ProtectedValue(await _protector
                .ProtectAsync(
                    await employee.ReferenceAsync(_subjects, ct).ConfigureAwait(false),
                    Encoding.UTF8.GetBytes(Reason!),
                    ct: ct)
                .ConfigureAwait(false));

        var request = LeaveRequest.Open(
            employee.Id, _currentUser.Id, employee.TeamId, AbsenceKindId, allowanceId, From, To, Hours, amount.Value, reason);
        _requests.Add(request);
        return LeaveRequestDto.FromEntity(request);
    }

    /// <summary>
    ///     What a request must say depends on how its kind is counted: hours of one day for a kind counted in
    ///     hours, no hours at all for one counted in days. A 422 with the fields, like the rules on the
    ///     properties.
    /// </summary>
    /// <remarks>
    ///     Called by the invoker right after it loads the kind, before <c>Execute</c>. It was an
    ///     <c>IAsyncValidator</c> that read the kind a second time, because a validator runs before the load
    ///     and cannot see the action's loaded entities.
    /// </remarks>
    private ValidationError ValidateLoaded()
    {
        if (_absenceKind.Unit == AbsenceUnit.Days)
            return Hours is null
                ? ValidationError.Valid
                : ValidationError.For(nameof(Hours), T.Validation.LeaveRequest.HoursOnAKindCountedInDays);

        var error = ValidationError.Valid;
        if (From != To)
            error = error.Combine(ValidationError.For(nameof(To), T.Validation.LeaveRequest.HoursAreOneDay));
        if (Hours is null)
            error = error.Combine(ValidationError.For(nameof(Hours), T.Validation.LeaveRequest.HoursOutOfRange));
        return error;
    }

    /// <summary>The hours asked for, if the one day of the request is a working day.</summary>
    private Result<decimal, IError> HoursOnTheDay()
    {
        // The validator refuses a kind counted in hours without them, or over more than one day.
        var hours = Ensure.ThrowIfNull(Hours);
        if (!_calendar.IsBusinessDay(new LocalDate(From), ItalianPublicHolidays.Country))
            return ValidationError.For(nameof(From), T.Validation.LeaveRequest.NoWorkingDays);

        return hours;
    }

    private async Task<Result<decimal, IError>> WorkingDaysAsync(CancellationToken ct)
    {
        // [From, To] inclusive: the calculator counts [from, to).
        var days = _calendar.CountBusinessDays(
            new LocalDate(From), new LocalDate(To).AddDays(1), ItalianPublicHolidays.Country);

        var closures = await _companyHolidays.FindBetweenAsync(From, To, ct).ConfigureAwait(false);
        days -= closures.Count(h => _calendar.IsBusinessDay(new LocalDate(h.Date), ItalianPublicHolidays.Country));

        if (days <= 0)
            return ValidationError.For(nameof(From), T.Validation.LeaveRequest.NoWorkingDays);

        return (decimal)days;
    }
}
