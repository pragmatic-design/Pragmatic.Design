using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Delivery priority options introduced in API v2.
/// </summary>
public enum DeliveryPriority { Standard, Express, Overnight }

/// <summary>
///     Demonstrates property-level API versioning with <c>[SinceVersion]</c>
///     and a dedicated <c>ExecuteV2()</c> override for version-specific logic.
/// </summary>
/// <remarks>
///     Generator produces:
///     <list type="bullet">
///       <item><c>VersionedCreateOrderV1Body</c> — <c>Product</c>, <c>Quantity</c> only</item>
///       <item><c>VersionedCreateOrderV2Body</c> — adds <c>SpecialInstructions</c> + <c>Priority</c></item>
///     </list>
///     Dispatch rules:
///     <list type="bullet">
///       <item>v1 route → <c>Execute(ct)</c></item>
///       <item>v2 route → <c>ExecuteV2(ct)</c></item>
///     </list>
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Post, "/orders")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public partial class VersionedCreateOrderAction : DomainAction<Guid, ValidationError>
{
    private IOrderRepository _orderRepository = null!;
    private IEmailService _emailService = null!;

    // ── v1 and v2 properties ─────────────────────────────────────────────────

    /// <summary>The product to order.</summary>
    public required string Product { get; init; }

    /// <summary>Number of units.</summary>
    public required int Quantity { get; init; }

    // ── v2-only properties (omitted from v1 body DTO) ────────────────────────

    /// <summary>
    ///     Special handling note added in v2.
    ///     Omitted from <c>VersionedCreateOrderV1Body</c>.
    /// </summary>
    [SinceVersion("2.0")]
    public string? SpecialInstructions { get; init; }

    /// <summary>
    ///     Delivery speed tier added in v2.
    ///     Omitted from <c>VersionedCreateOrderV1Body</c>.
    /// </summary>
    [SinceVersion("2.0")]
    public DeliveryPriority Priority { get; init; } = DeliveryPriority.Standard;

    // ── Handlers ─────────────────────────────────────────────────────────────

    /// <summary>Called for API v1 requests.</summary>
    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Product))
            return new ValidationError { Field = nameof(Product), Message = "Product is required." };

        var order = await _orderRepository.CreateAsync(Product, Quantity, ct);
        await _emailService.SendAsync("admin@example.com", "New Order", $"Order {order.Id}", ct);

        return order.Id;
    }

    /// <summary>
    ///     Called for API v2 requests. Has access to <see cref="SpecialInstructions"/>
    ///     and <see cref="Priority"/>.
    /// </summary>
    public async Task<Result<Guid, IError>> ExecuteV2(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Product))
            return new ValidationError { Field = nameof(Product), Message = "Product is required." };

        var order = await _orderRepository.CreateAsync(Product, Quantity, ct);

        // v2-enhanced: include priority in the confirmation email
        var subject = Priority == DeliveryPriority.Overnight
            ? "[OVERNIGHT] New Order"
            : "New Order";

        var body = $"Order {order.Id} — priority: {Priority}" +
                   (SpecialInstructions is not null ? $" — notes: {SpecialInstructions}" : string.Empty);

        await _emailService.SendAsync("admin@example.com", subject, body, ct);

        return order.Id;
    }
}
