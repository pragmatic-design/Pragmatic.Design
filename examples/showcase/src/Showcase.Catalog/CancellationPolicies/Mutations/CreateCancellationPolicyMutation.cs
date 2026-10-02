namespace Showcase.Catalog.CancellationPolicies.Mutations;

/// <summary>
/// Creates a new cancellation policy under a property.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] with FK property (PropertyId).
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/cancellation-policies")]
[RequirePermission(CatalogPermissions.CancellationPolicy.Create)]
[ReturnsDto<CancellationPolicyCreatedDto>]
public partial class CreateCancellationPolicyMutation : Mutation<CancellationPolicy>
{
    public required Guid PropertyId { get; init; }
    public required string Name { get; init; }
    public int? HoursBeforeCheckIn { get; init; }
    public required decimal PenaltyPercentage { get; init; }
    public bool? IsDefault { get; init; }
}
