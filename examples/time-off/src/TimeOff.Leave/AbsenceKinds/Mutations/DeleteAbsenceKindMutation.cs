namespace TimeOff.Leave.AbsenceKinds.Mutations;

/// <summary>
///     HR deletes a kind of absence created by mistake.
/// </summary>
/// <remarks>
///     Refused while an allowance or a leave request uses the kind: both relations restrict the delete,
///     and the refusal names what uses it. Nothing here checks it by hand — the declared rule does.
/// </remarks>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(LeavePermissions.AbsenceKind.Delete)]
[Endpoint(HttpVerb.Delete, "api/absence-kinds/{id}")]
public partial class DeleteAbsenceKindMutation : Mutation<AbsenceKind>
{
    public required Guid Id { get; init; }
}
