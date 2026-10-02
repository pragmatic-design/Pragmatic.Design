namespace TimeOff.Leave.Employees.Actions;

/// <summary>
///     Everything the application holds about the signed-in employee — an access request, answered
///     without anyone having to assemble it by hand.
/// </summary>
/// <remarks>
///     What is collected is decided by the classification, not here: every <c>[PersonalData]</c> field of
///     every entity that leads to the employee, through the sources the generator writes from it. A field
///     classified tomorrow is in tomorrow's export with no change to this operation.
/// </remarks>
[DomainAction]
[RequirePermission(LeavePermissions.OwnProfile.Manage)]
[ProcessesData<LeaveRequest>]
[LoadCurrentUser]
[Endpoint(HttpVerb.Get, "api/me/personal-data")]
public partial class ExportMyPersonalDataAction : DomainAction<PersonalDataExportDto, NotFoundError>
{
    private ISubjectRegistry _subjects = null!;
    private ISubjectAccess _access = null!;

    /// <remarks>
    ///     ⚠️ <b>One classified field is not here, and saying so is part of the answer.</b>
    ///     <c>LeaveRequest.Reason</c> is stored encrypted under the employee's own key, and the
    ///     generated extractor performs no cryptography — for the same reason the value converter does
    ///     not: reading has three outcomes and one of them is "this subject was erased", which neither
    ///     an extractor nor a converter can express. It is obtained through
    ///     <c>GET /api/leave-requests/{id}/reason</c>, which can. A generated export that returned the
    ///     ciphertext would be worse than one that leaves it out, and one that returned an empty string
    ///     would say the employee gave no reason.
    /// </remarks>
    public override async Task<Result<PersonalDataExportDto, IError>> Execute(CancellationToken ct = default)
    {
        var subjectRef = await _currentEmployee.ReferenceAsync(_subjects, ct).ConfigureAwait(false);
        var export = await _access.CollectAsync(subjectRef, ct).ConfigureAwait(false);

        return PersonalDataExportDto.From(export);
    }
}
