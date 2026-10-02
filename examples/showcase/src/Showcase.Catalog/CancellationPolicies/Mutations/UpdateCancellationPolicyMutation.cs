namespace Showcase.Catalog.CancellationPolicies.Mutations;

/// <summary>
/// Updates a cancellation policy.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] — all nullable for partial update.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Put, "api/cancellation-policies/{id}")]
[RequirePermission(CatalogPermissions.CancellationPolicy.Update)]
public partial class UpdateCancellationPolicyMutation : Mutation<CancellationPolicy>
{
    public required Guid Id { get; init; }
    public string? Name { get; init; }
    public int? HoursBeforeCheckIn { get; init; }
    public decimal? PenaltyPercentage { get; init; }
    public bool? IsDefault { get; init; }
}
