namespace TimeOff.Leave.AbsenceKinds.Mutations;

/// <summary>
///     HR defines a kind of absence: its code, its name in each language, and how it is counted.
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(LeavePermissions.AbsenceKind.Create)]
[Endpoint(HttpVerb.Post, "api/absence-kinds")]
[CreatedAt("/api/absence-kinds/{Id}")]
[ReturnsDto<AbsenceKindDto>]
public partial class CreateAbsenceKindMutation : Mutation<AbsenceKind>
{
    public required string Code { get; init; }

    /// <summary>The name in as many cultures as HR writes it: <c>{ "en-US": "Vacation", "it-IT": "Ferie" }</c>.</summary>
    public required LocalizedString Name { get; init; }

    public required AbsenceUnit Unit { get; init; }

    public required bool UsesAllowance { get; init; }

    public override Task<Result<AbsenceKind, IError>> ApplyAsync(AbsenceKind entity, CancellationToken ct = default)
    {
        var rule = entity.CheckCountingRule();
        return Task.FromResult(rule.IsFailure
            ? Result<AbsenceKind, IError>.Failure(rule)
            : Result<AbsenceKind, IError>.Success(entity));
    }
}
