namespace TimeOff.Leave.Allowances.Mutations;

/// <summary>
///     HR corrects an allowance: what is granted, or what carried over.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.Allowance.Update)]
[Endpoint(HttpVerb.Put, "api/allowances/{id}")]
[ReturnsDto<AllowanceDto>]
public partial class CorrectAllowanceMutation : Mutation<Allowance>
{
    public required Guid Id { get; init; }

    public decimal? Entitled { get; init; }

    public decimal? CarriedOver { get; init; }
}
