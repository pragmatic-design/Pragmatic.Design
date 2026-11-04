using Pragmatic.Actions.Mutation;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Result;

namespace PragmaticEntities.Tests;

/// <summary>A create of a temporal entity with a constraint: the invoker checks it against the stretches already written (#512).</summary>
[Mutation(Mode = MutationMode.Create)]
public partial class TakeDeskDutyMutation : Mutation<DeskDuty>
{
    public required Guid PropertyId { get; init; }

    [MapIgnore]
    public required string Person { get; init; }

    [MapIgnore]
    public required DateTimeOffset From { get; init; }

    // The temporal columns are public, with no generated setter to map onto: set here, as the Showcase's CreateStaffAssignmentMutation does.
    public override Task<Result<DeskDuty, IError>> ApplyAsync(DeskDuty entity, CancellationToken ct = default)
    {
        entity.Person = Person;
        entity.ValidFrom = From;
        return Task.FromResult<Result<DeskDuty, IError>>(entity);
    }
}
