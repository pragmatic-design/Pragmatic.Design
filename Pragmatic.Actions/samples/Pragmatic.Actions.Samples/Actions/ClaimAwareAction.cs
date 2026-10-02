using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     DTO returned for the current user's order history.
/// </summary>
public record UserOrderHistoryDto(Guid UserId, Guid? TenantId, IReadOnlyList<Guid> OrderIds);

/// <summary>
///     Returns order history for the authenticated user.
///     Demonstrates <c>[FromClaim]</c> binding on a DomainAction exposed as an HTTP endpoint.
/// </summary>
/// <remarks>
///     Claim binding rules:
///     <list type="bullet">
///       <item><c>UserId</c> — required; if the "sub" claim is absent → 401 Unauthorized</item>
///       <item><c>TenantId</c> — optional; null when the "tenant_id" claim is absent</item>
///     </list>
///     The action can also be invoked programmatically from other actions or background jobs —
///     in that case populate <c>UserId</c> and <c>TenantId</c> manually before calling <c>Execute</c>.
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Get, "/me/orders")]
[Pragmatic.Endpoints.Attributes.ApiSummary("My Order History")]
[Pragmatic.Endpoints.Attributes.ApiTags("Orders", "Profile")]
public partial class ClaimAwareAction : DomainAction<UserOrderHistoryDto, NotFoundError>
{
    private IOrderRepository _orderRepository = null!;

    /// <summary>
    ///     Authenticated user's ID. Extracted from the JWT "sub" (subject) claim.
    ///     Required — 401 if absent.
    /// </summary>
    [FromClaim("sub")]
    public Guid UserId { get; set; }

    /// <summary>
    ///     Optional tenant scope. Extracted from the JWT "tenant_id" claim.
    ///     Null when the claim is not present in the token.
    /// </summary>
    [FromClaim("tenant_id")]
    public Guid? TenantId { get; set; }

    /// <inheritdoc />
    public override async Task<Result<UserOrderHistoryDto, IError>> Execute(CancellationToken ct = default)
    {
        // The repository can be filtered by TenantId when multi-tenancy applies.
        var orderIds = await _orderRepository.GetIdsByUserAsync(UserId, TenantId, ct);

        return new UserOrderHistoryDto(UserId, TenantId, orderIds);
    }
}
