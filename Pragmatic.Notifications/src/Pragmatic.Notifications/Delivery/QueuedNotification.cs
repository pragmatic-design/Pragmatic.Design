namespace Pragmatic.Notifications.Delivery;

/// <summary>
///     Channel item for background delivery: the request, the Id of the Pending tracking record
///     created by <c>EnqueueAsync</c> so the pipeline can settle its final status, and the tenant the
///     notification was enqueued under.
/// </summary>
/// <remarks>
///     The tenant travels with the item because delivery happens outside the originating request: the
///     worker has no ambient tenant of its own, so a recipient resolver reading tenant-scoped data
///     would otherwise query with a null tenant and find nothing — or, worse, read across tenants.
///     Same approach as the job processor and the messaging outbox.
/// </remarks>
internal readonly record struct QueuedNotification(
    NotificationRequest Request,
    Guid TrackingId,
    string? TenantId);
