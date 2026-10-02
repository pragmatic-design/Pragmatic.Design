namespace TimeOff.Leave.AbsenceKinds.Mutations;

/// <summary>
///     HR renames a kind or changes how it is counted. Only what is sent changes; the counting rule
///     holds on the result.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(LeavePermissions.AbsenceKind.Update)]
[Endpoint(HttpVerb.Put, "api/absence-kinds/{id}")]
[ReturnsDto<AbsenceKindDto>]
public partial class UpdateAbsenceKindMutation : Mutation<AbsenceKind>
{
    public required Guid Id { get; init; }

    public LocalizedString? Name { get; init; }

    public AbsenceUnit? Unit { get; init; }

    public bool? UsesAllowance { get; init; }

    public override Task<Result<AbsenceKind, IError>> ApplyAsync(AbsenceKind entity, CancellationToken ct = default)
    {
        var rule = entity.CheckCountingRule();
        return Task.FromResult(rule.IsFailure
            ? Result<AbsenceKind, IError>.Failure(rule)
            : Result<AbsenceKind, IError>.Success(entity));
    }
}
