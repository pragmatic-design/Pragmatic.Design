using System.ComponentModel.DataAnnotations;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Unique identifier for a placed order.
/// </summary>
public record OrderId(Guid Value);

/// <summary>
///     Delivery priority options introduced in v2.
/// </summary>
public enum DeliveryPriority { Standard, Express, Overnight }

/// <summary>
///     Demonstrates <c>[SinceVersion]</c> for property-level API versioning combined
///     with <c>ExecuteV2()</c> for version-specific dispatch on a DomainAction endpoint.
/// </summary>
/// <remarks>
///     Generator produces two body DTOs:
///     <list type="bullet">
///       <item><c>PlaceOrderV1Body</c> — <c>CustomerId</c> + <c>Items</c> only</item>
///       <item><c>PlaceOrderV2Body</c> — adds <c>SpecialInstructions</c> + <c>Priority</c></item>
///     </list>
///     Dispatch rules:
///     <list type="bullet">
///       <item>v1 route → binds V1 body → calls <c>Execute(ct)</c></item>
///       <item>v2 route → binds V2 body → calls <c>ExecuteV2(ct)</c> if defined</item>
///     </list>
/// </remarks>
// [ApiVersion] is NOT required — the SG infers versions from ExecuteV{n} methods automatically.
// Use [ApiVersion] only if you need Deprecated/SunsetDate metadata.
[DomainAction]
[Endpoint(HttpVerb.Post, "/orders")]
[ApiSummary("Place Order")]
[ApiDescription("Places a new order. v2 adds delivery priority and special instructions.")]
[ApiTags("Orders")]
[HttpStatus(201)]
public partial class VersionedOrderEndpoint : DomainAction<OrderId, ValidationError>
{
    // ── Present in both v1 and v2 ───────────────────────────────────────────

    /// <summary>Customer placing the order.</summary>
    [Required]
    public required Guid CustomerId { get; init; }

    /// <summary>Items to include.</summary>
    [Required]
    [MinLength(1)]
    public required List<string> Items { get; init; }

    // ── v2-only properties (omitted from v1 body DTO) ───────────────────────

    /// <summary>
    ///     Special handling instructions. Introduced in API v2.
    /// </summary>
    [SinceVersion("2.0")]
    [StringLength(500)]
    public string? SpecialInstructions { get; init; }

    /// <summary>
    ///     Delivery priority level. Introduced in API v2.
    /// </summary>
    [SinceVersion("2.0")]
    public DeliveryPriority Priority { get; init; } = DeliveryPriority.Standard;

    // ── Handlers ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>Called for v1 requests.</remarks>
    public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
    {
        // Standard order placement logic.
        var orderId = new OrderId(Guid.NewGuid());
        return Task.FromResult<Result<OrderId, IError>>(orderId);
    }

    /// <summary>
    ///     Called instead of <see cref="Execute"/> for v2 requests.
    ///     Has access to <see cref="SpecialInstructions"/> and <see cref="Priority"/>.
    /// </summary>
    public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
    {
        // Enhanced logic: could route based on Priority, log SpecialInstructions, etc.
        var orderId = new OrderId(Guid.NewGuid());
        return Task.FromResult<Result<OrderId, IError>>(orderId);
    }
}
