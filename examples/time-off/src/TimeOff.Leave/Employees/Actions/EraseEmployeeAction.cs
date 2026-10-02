using Pragmatic.Cryptography;

namespace TimeOff.Leave.Employees.Actions;

/// <summary>
///     Erases an employee's personal data: each classified field as its classification says, the account
///     closed, and the employee's reference left leading to no one.
/// </summary>
/// <remarks>
///     <para>
///         The rows stay — the requests still count against the year, the decisions still say who took
///         them — and name nobody. The audit trail is not touched: it never held more than references,
///         and it verifies after the erasure as it did before.
///     </para>
///     <para>
///         When to erase is HR's decision, not the application's: someone leaves, and the data is kept
///         for as long as the company is obliged to. This is the moment that obligation ends — so only
///         for an employee who has left (<see cref="EmployeeStillActiveError" /> otherwise), and the
///         employee is read past the filter that hides the ones who have.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.PersonalData.Erase)]
[ProcessesData<LeaveRequest>]
[WithoutFilter<Employee>]
[LoadEntity<Employee>(nameof(Id))]
[Endpoint(HttpVerb.Post, "api/employees/{id}/erasure")]
public partial class EraseEmployeeAction : DomainAction<ErasureDto, NotFoundError, EmployeeStillActiveError>
{
    private ISubjectRegistry _subjects = null!;
    private ISubjectErasure _erasure = null!;

    /// <summary>The per-subject keys, one of which is how <c>LeaveRequest.Reason</c> is erased.</summary>
    private ISubjectKeyStore _keys = null!;

    /// <summary>Read to answer one question: is there anything of this employee's under a key?</summary>
    private IReadRepository<LeaveRequest> _requests = null!;

    [FromRoute]
    public required Guid Id { get; init; }

    public override async Task<Result<ErasureDto, IError>> Execute(CancellationToken ct = default)
    {
        if (!_employee.IsDeleted)
            return new EmployeeStillActiveError();

        var subjectRef = await _employee.ReferenceAsync(_subjects, ct).ConfigureAwait(false);

        // ⚠️ The key destroyer is not optional for this application: LeaveRequest.Reason is
        // ErasureStrategy.DestroyKey, so the generated plan deliberately skips that column — clearing
        // it is not how it is erased — and destroying the key is the erasure. The
        // orchestrator refuses rather than reporting an erasure it did not perform, so leaving this out
        // fails loudly instead of quietly.
        // ⚠️ Whether this employee has a key is the application's to know, and the store is right to
        // refuse otherwise: "destroying a key that was never created would report an erasure that did
        // not happen" — the same principle as the orchestrator's, one level down. An employee who never gave a
        // reason for any request has nothing protected, so there is nothing to destroy and saying so is
        // not a failure.
        var reasons = await _requests
            .FindAsync(LeaveRequestSpecifications.RequestedBy(_employee.Id), ct)
            .ConfigureAwait(false);
        var hasProtectedData = reasons.Any(r => !r.Reason.IsEmpty);

        var outcome = await _erasure
            .EraseAsync(
                subjectRef,
                async (subject, token) =>
                {
                    if (!hasProtectedData)
                        return false;

                    // Idempotent for a key that exists: a second erasure reports AlreadyDestroyed
                    // rather than failing, and either way the key is gone.
                    await _keys.DestroyAsync(subject, token).ConfigureAwait(false);
                    return true;
                },
                ct)
            .ConfigureAwait(false);

        return ErasureDto.From(outcome);
    }
}
