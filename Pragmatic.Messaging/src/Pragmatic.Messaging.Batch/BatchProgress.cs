using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Tracks progress of a batch operation.
///     Queryable for dashboard/monitoring.
/// </summary>
/// <remarks>
///     Implements <see cref="ITenantEntity"/> for row-level tenant isolation (mirrors the saga table):
///     the EF store stamps <see cref="TenantId"/> from the ambient tenant on create and, in a
///     multi-tenant host, the generated boundary DbContext adds a fail-closed EF query filter.
///     Single-tenant hosts leave it empty and no filter is emitted.
/// </remarks>
public sealed class BatchProgress : ITenantEntity
{
    /// <summary>Unique batch operation ID.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Total items in the batch.</summary>
    public int Total { get; set; }

    /// <summary>Successfully completed items.</summary>
    public int Completed { get; set; }

    /// <summary>Failed items (sent to dead letter).</summary>
    public int Failed { get; set; }

    /// <summary>Items still pending.</summary>
    public int Pending => Total - Completed - Failed;

    /// <summary>Whether the batch is fully processed.</summary>
    public bool IsComplete => Completed + Failed >= Total;

    /// <summary>Completion percentage (0-100).</summary>
    public double ProgressPercent => Total > 0 ? (double)(Completed + Failed) / Total * 100 : 0;

    /// <summary>When the batch started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the batch completed (null if still in progress).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    ///     Number of items already published by the dispatcher — a crash-resume checkpoint.
    ///     On the sequential dispatch path (<see cref="BatchDispatchOptions.MaxConcurrency"/> = 1) items are
    ///     published in order, so a resume can skip the first <see cref="DispatchedCount"/> items instead of
    ///     re-publishing everything or leaving the batch orphaned. Not a fine-grained checkpoint for the
    ///     parallel path (out-of-order publishes cannot be captured by a single counter).
    /// </summary>
    public int DispatchedCount { get; set; }

    /// <summary>
    ///     Tenant this batch belongs to. Stamped from the ambient tenant on create by the EF store;
    ///     empty in single-tenant hosts.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Descriptive label for the batch (e.g., "RecalculatePrices 2026-03-25").</summary>
    public string? Label { get; set; }
}
