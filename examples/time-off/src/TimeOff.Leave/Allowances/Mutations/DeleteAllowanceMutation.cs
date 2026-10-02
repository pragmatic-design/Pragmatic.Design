namespace TimeOff.Leave.Allowances.Mutations;

/// <summary>
///     HR deletes an allowance granted by mistake — to the wrong person, for the wrong kind or year.
/// </summary>
/// <remarks>
///     Refused while a leave request draws on it: the allowance declares its requests with
///     <c>OnDelete = Restrict</c>, and the refusal names them. An allowance nothing has drawn on goes.
/// </remarks>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(LeavePermissions.Allowance.Delete)]
[Endpoint(HttpVerb.Delete, "api/allowances/{id}")]
public partial class DeleteAllowanceMutation : Mutation<Allowance>
{
    public required Guid Id { get; init; }
}
