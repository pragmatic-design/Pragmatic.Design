using Pragmatic.Actions.Abstractions;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Returns the authenticated user's own orders, binding the user ID directly
///     from the JWT "sub" claim. Demonstrates <c>[FromClaim]</c> binding for both
///     required (non-nullable) and optional (nullable) claims.
/// </summary>
/// <remarks>
///     Generator behavior:
///     - <c>UserId</c> is required: if "sub" claim is absent → 401 Unauthorized.
///     - <c>TenantId</c> is optional: if "tenant_id" claim is absent → null.
///     - No body DTO generated because all public properties have explicit binding attributes.
/// </remarks>
[Endpoint(HttpVerb.Get, "/me/orders")]
[ApiSummary("Get My Orders")]
[ApiDescription("Returns orders belonging to the authenticated user, extracted from JWT claims.")]
[ApiTags("Orders", "Profile")]
public partial class ClaimBoundEndpoint : DomainAction<OrderResponse[], NotFoundError>
{
    /// <summary>
    ///     User ID extracted from the JWT "sub" (subject) claim.
    ///     Required — returns 401 if the claim is absent.
    /// </summary>
    [FromClaim("sub")]
    public Guid UserId { get; set; }

    /// <summary>
    ///     Tenant ID extracted from the "tenant_id" claim.
    ///     Optional — null if the claim is not present in the token.
    /// </summary>
    [FromClaim("tenant_id")]
    public Guid? TenantId { get; set; }

    /// <inheritdoc />
    public override Task<Result<OrderResponse[], IError>> Execute(CancellationToken ct = default)
    {
        // In a real endpoint, call a repository filtered by UserId and TenantId.
        // Returning a stub result for sample clarity.
        OrderResponse[] orders =
        [
            new OrderResponse(Guid.NewGuid(), UserId.ToString(), 99.99m, DateTimeOffset.UtcNow)
        ];

        return Task.FromResult<Result<OrderResponse[], IError>>(orders);
    }
}
