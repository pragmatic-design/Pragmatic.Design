namespace TimeOff.Leave.AbsenceKinds.Queries;

/// <summary>
///     The kinds of absence, by code — what anyone asking for leave chooses from.
/// </summary>
/// <remarks>
///     Every employee reads them: the permission is in every role. Declared all the same — an endpoint
///     that asked for nothing beyond a token would be open to any role added later.
/// </remarks>
[Query<AbsenceKind, AbsenceKindDto>(Paged = true)]
[RequirePermission(LeavePermissions.AbsenceKind.Read)]
[Endpoint(HttpVerb.Get, "api/absence-kinds")]
public partial class ListAbsenceKindsQuery
{
    [Filter]
    public string? Code { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? CodeSort { get; init; }
}
